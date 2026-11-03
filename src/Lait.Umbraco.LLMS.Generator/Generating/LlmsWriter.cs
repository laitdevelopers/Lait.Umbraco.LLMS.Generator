using System.Text;
using System.Text.RegularExpressions;
using Lait.Umbraco.LLMS.Generator.Models;

namespace Lait.Umbraco.LLMS.Generator.Generating;

/// <summary>A published, routable page as the writer sees it (already resolved for the site's culture).</summary>
public sealed record PageInfo(Guid Key, string Name, string Url, string? Description, Guid ContentTypeKey, bool Hidden);

/// <summary>Turns a config into llms.txt (llmstxt.org format). Content access is passed in, so this has no Umbraco dependency.</summary>
public static partial class LlmsWriter
{
    /// <summary>Hard cap on links from one section, whatever the editor sets: keeps a whole-site section from producing a huge file.</summary>
    public const int SectionCap = 1000;

    /// <param name="page">The page, or null when it is unpublished or not routable in this culture.</param>
    /// <param name="children">Child keys in sort order.</param>
    public static async Task<string> RenderAsync(LlmsConfig config, Func<Guid, Task<PageInfo?>> page, Func<Guid, IEnumerable<Guid>> children)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(OneLine(config.Title)).Append('\n');
        if (!string.IsNullOrWhiteSpace(config.Summary)) sb.Append("\n> ").Append(OneLine(config.Summary)).Append('\n');

        var inList = false;
        void Paragraph(string text) { sb.Append('\n').Append(text).Append('\n'); inList = false; }
        void Item(string line) { if (!inList) sb.Append('\n'); sb.Append(line).Append('\n'); inList = true; }

        foreach (var b in config.Blocks)
        {
            switch (b.Type)
            {
                case BlockType.Text when !string.IsNullOrWhiteSpace(b.Text):
                    Paragraph(b.Text.Trim().Replace("\r\n", "\n"));
                    break;
                case BlockType.Heading when !string.IsNullOrWhiteSpace(b.Text):
                    Paragraph("## " + OneLine(b.Text));
                    break;
                case BlockType.Link:
                    if (b.Node is { } key)
                    {
                        if (await page(key) is { } p)
                            Item(Line(Or(b.Text, p.Name), p.Url, Or(b.Description, p.Description)));
                    }
                    else if (!string.IsNullOrWhiteSpace(b.Url))
                        Item(Line(b.Text, b.Url.Trim(), b.Description));
                    break;
                case BlockType.Section when b.Node is { } root:
                    if (!string.IsNullOrWhiteSpace(b.Text)) Paragraph("## " + OneLine(b.Text));
                    await foreach (var p in SectionPages(b, root, page, children))
                        Item(Line(p.Name, p.Url, p.Description));
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Depth-first in sort order, so a page's children follow the page.</summary>
    static async IAsyncEnumerable<PageInfo> SectionPages(LlmsBlock b, Guid root, Func<Guid, Task<PageInfo?>> page, Func<Guid, IEnumerable<Guid>> children)
    {
        var maxDepth = b.IncludeDescendants ? b.MaxDepth ?? int.MaxValue : 1;
        var maxPages = Math.Min(b.MaxPages ?? SectionCap, SectionCap);
        var count = 0;
        var stack = new Stack<(Guid Key, int Depth)>(children(root).Reverse().Select(k => (k, 1)));

        while (stack.Count > 0 && count < maxPages)
        {
            var (key, depth) = stack.Pop();
            var p = await page(key);
            // Unpublished and hidden pages take their subtree with them; unpublished children are not reachable anyway.
            if (p is null || (b.SkipHidden && p.Hidden)) continue;

            if ((b.IncludeTypes.Count == 0 || b.IncludeTypes.Contains(p.ContentTypeKey)) && !b.ExcludeTypes.Contains(p.ContentTypeKey))
            {
                count++;
                yield return p;
            }
            if (depth < maxDepth)
                foreach (var child in children(key).Reverse()) stack.Push((child, depth + 1));
        }
    }

    static string Line(string title, string url, string? description)
    {
        var line = $"- [{EscapeLinkText(OneLine(title))}]({url.Replace(" ", "%20").Replace(")", "%29")})";
        return string.IsNullOrWhiteSpace(description) ? line : line + ": " + OneLine(StripHtml(description));
    }

    static string Or(string value, string? fallback) => string.IsNullOrWhiteSpace(value) ? fallback ?? "" : value;
    static string OneLine(string s) => Whitespace().Replace(s, " ").Trim();
    static string EscapeLinkText(string s) => s.Replace("[", "\\[").Replace("]", "\\]");
    /// <summary>Description properties are often rich text.</summary>
    static string StripHtml(string s) => System.Net.WebUtility.HtmlDecode(Tags().Replace(s, " "));

    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
    [GeneratedRegex("<[^>]*>")] private static partial Regex Tags();

    public const int MaxBlocks = 200;

    /// <summary>Checks what the dashboard submits and trims it. Errors are localization keys.</summary>
    public static List<LlmsError> Validate(LlmsConfig c)
    {
        var errors = new List<LlmsError>();
        c.Title = c.Title?.Trim() ?? "";
        c.Summary = c.Summary?.Trim() ?? "";
        c.DescriptionAliases = Aliases(c.DescriptionAliases);
        c.HiddenAliases = Aliases(c.HiddenAliases);
        c.Blocks ??= [];

        if (c.Title.Length == 0) errors.Add(new("laitLlms_errorTitle"));
        if (c.Blocks.Count > MaxBlocks) errors.Add(new("laitLlms_errorTooManyBlocks"));

        for (var i = 0; i < c.Blocks.Count; i++)
        {
            var b = c.Blocks[i];
            var n = i + 1;
            b.Text = b.Text?.Trim() ?? "";
            b.Url = b.Url?.Trim() ?? "";
            b.Description = b.Description?.Trim() ?? "";
            b.IncludeTypes ??= [];
            b.ExcludeTypes ??= [];

            if (!BlockType.All.Contains(b.Type)) { errors.Add(new("laitLlms_errorType", n)); continue; }
            if (b.Type is BlockType.Text or BlockType.Heading && b.Text.Length == 0) errors.Add(new("laitLlms_errorText", n));
            if (b.Type == BlockType.Section && b.Node is null) errors.Add(new("laitLlms_errorNode", n));
            if (b.Type == BlockType.Link && b.Node is null)
            {
                var ok = Uri.TryCreate(b.Url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
                if (!ok) errors.Add(new("laitLlms_errorUrl", n));
                if (b.Text.Length == 0) errors.Add(new("laitLlms_errorLinkTitle", n));
            }
            if (b.MaxDepth is < 1 || b.MaxPages is < 1) errors.Add(new("laitLlms_errorLimits", n));
        }
        return errors;
    }

    static List<string> Aliases(List<string>? list) =>
        (list ?? []).Select(a => a.Trim()).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
