using Lait.Umbraco.LLMS.Generator.Models;
using Lait.Umbraco.LLMS.Generator.Persistence;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Lait.Umbraco.LLMS.Generator.Generating;

/// <summary>A node that gets its own llms.txt: a node with domains, or a root node when the install has no domains.</summary>
public sealed record SiteInfo(Guid Key, string Name, IReadOnlyList<string> Domains, bool Configured);

/// <summary>Resolves which site a request is for and renders its llms.txt from published content.</summary>
public sealed class LlmsService(
    LlmsStore store, IDomainService domains, IContentService contentService, IIdKeyMap idKeys, ILanguageService languages,
    IPublishedContentCache cache, IDocumentNavigationQueryService navigation, IPublishedUrlProvider urls,
    IUmbracoContextFactory umbracoContextFactory, AppCaches appCaches)
{
    const string CachePrefix = "lait-llms:";
    sealed record Cached(string? Text);

    /// <summary>A domain binding with the host it answers on. Culture-only (wildcard) bindings have no host and are skipped.</summary>
    sealed record Binding(Guid Root, string Host, string Path, string? Culture, string Name);

    async Task<List<Binding>> BindingsAsync() => (await domains.GetAllAsync(false))
        .Where(d => d.RootContentId is not null && !d.DomainName.StartsWith('*'))
        .Select(d => (d, ok: Uri.TryCreate(d.DomainName.Contains("://") ? d.DomainName : "https://" + d.DomainName, UriKind.Absolute, out var u), u))
        .Where(x => x.ok && idKeys.GetKeyForId(x.d.RootContentId!.Value, UmbracoObjectTypes.Document).Success)
        .Select(x => new Binding(idKeys.GetKeyForId(x.d.RootContentId!.Value, UmbracoObjectTypes.Document).Result,
            x.u!.Host, x.u.AbsolutePath, x.d.LanguageIsoCode, x.d.DomainName))
        .ToList();

    public async Task<IReadOnlyList<SiteInfo>> SitesAsync()
    {
        var configured = store.ConfiguredRoots();
        var bindings = await BindingsAsync();
        var keys = bindings.Count > 0
            ? bindings.Select(b => b.Root).Distinct()
            : contentService.GetRootContent().Select(c => c.Key);
        return keys.Select(k => contentService.GetById(k)).OfType<IContent>()
            .Select(c => new SiteInfo(c.Key, c.Name ?? "", bindings.Where(b => b.Root == c.Key).Select(b => b.Name).ToList(), configured.Contains(c.Key)))
            .ToList();
    }

    /// <summary>The llms.txt for the request's host, or null (404) when the host has no site or the site has no saved config.</summary>
    public async Task<string?> ForRequestAsync(Uri request)
    {
        var key = CachePrefix + request.Host.ToLowerInvariant();
        if (appCaches.RuntimeCache.Get(key) is Cached hit) return hit.Text;

        string? text = null;
        if (await SiteForRequestAsync(request) is { } site && store.Get(site.Root) is { } config)
            text = await RenderAsync(config, site.Culture, site.BaseUrl);

        // ponytail: other servers in a load-balanced setup only see a saved config after this expiry (publishing clears everywhere).
        appCaches.RuntimeCache.Insert(key, () => new Cached(text), TimeSpan.FromHours(1));
        return text;
    }

    /// <summary>Renders an (unsaved) config as it would look on the site's first domain.</summary>
    public async Task<string> PreviewAsync(Guid root, LlmsConfig config, Uri fallbackBase)
    {
        var binding = (await BindingsAsync()).Where(b => b.Root == root).OrderBy(b => b.Path.Length).FirstOrDefault();
        var culture = binding?.Culture ?? await languages.GetDefaultIsoCodeAsync();
        var baseUrl = binding is null ? fallbackBase : new Uri($"https://{binding.Host}/");
        return await RenderAsync(config, culture, baseUrl);
    }

    public void ClearCache() => appCaches.RuntimeCache.ClearByKey(CachePrefix);

    async Task<(Guid Root, string? Culture, Uri BaseUrl)?> SiteForRequestAsync(Uri request)
    {
        var host = request.Host;
        var baseUrl = new Uri(request.GetLeftPart(UriPartial.Authority) + "/");
        var bindings = await BindingsAsync();
        if (bindings.Count == 0)
            return contentService.GetRootContent().FirstOrDefault() is { } first ? (first.Key, await languages.GetDefaultIsoCodeAsync(), baseUrl) : null;

        // ponytail: llms.txt lives at the host root, so of example.com and example.com/en the shortest path wins.
        var match = bindings.Where(b => b.Host.Equals(host, StringComparison.OrdinalIgnoreCase)).OrderBy(b => b.Path.Length).FirstOrDefault();
        return match is null ? null : (match.Root, match.Culture ?? await languages.GetDefaultIsoCodeAsync(), baseUrl);
    }

    async Task<string> RenderAsync(LlmsConfig config, string? culture, Uri baseUrl)
    {
        // The URL provider needs an UmbracoContext, which a backoffice API request does not have.
        using var umbracoContext = umbracoContextFactory.EnsureUmbracoContext();
        return await LlmsWriter.RenderAsync(config, key => PageAsync(key, config, culture, baseUrl),
            key => navigation.TryGetChildrenKeys(key, out var children) ? children : []);
    }

    async Task<PageInfo?> PageAsync(Guid key, LlmsConfig config, string? culture, Uri baseUrl)
    {
        var content = await cache.GetByIdAsync(key);   // published only
        if (content is null) return null;
        var variant = content.ContentType.VariesByCulture();
        if (variant && !content.IsPublished(culture)) return null;

        var url = urls.GetUrl(content, UrlMode.Absolute, variant ? culture : null, baseUrl);
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) return null;   // "#" = not routable

        var name = variant && culture is not null && content.Cultures.FirstOrDefault(c => c.Key.Equals(culture, StringComparison.OrdinalIgnoreCase)).Value is { } info
            ? info.Name : content.Name;

        var description = config.DescriptionAliases.Select(a => Value(content, a, culture)?.ToString()).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        var hidden = config.HiddenAliases.Any(a => Value(content, a, culture) is true or "1" or "true" or "True" or 1);
        return new PageInfo(content.Key, name, url, description, content.ContentType.Key, hidden);
    }

    static object? Value(IPublishedContent content, string alias, string? culture) =>
        content.GetProperty(alias) is { } p ? p.GetValue(p.PropertyType.VariesByCulture() ? culture : null) : null;
}
