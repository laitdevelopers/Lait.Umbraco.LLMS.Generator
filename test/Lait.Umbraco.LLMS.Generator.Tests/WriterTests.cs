using Lait.Umbraco.LLMS.Generator.Generating;
using Lait.Umbraco.LLMS.Generator.Models;

namespace Lait.Umbraco.LLMS.Generator.Tests;

public class WriterTests
{
    static readonly Guid PageType = Guid.NewGuid(), NewsType = Guid.NewGuid();

    /// <summary>
    /// Docs ─┬─ Intro
    ///       ├─ Guides ─┬─ Install
    ///       │          └─ Upgrade ── Deep
    ///       ├─ Secret (hidden) ── Under secret
    ///       └─ Draft (unpublished) ── Under draft
    /// </summary>
    sealed class Tree
    {
        readonly Dictionary<Guid, PageInfo?> pages = new();
        readonly Dictionary<Guid, List<Guid>> kids = new();
        public readonly Dictionary<string, Guid> Keys = new();

        public Tree()
        {
            Add("Docs", null);
            Add("Intro", "Docs", "Start <b>here</b>\n now");
            Add("Guides", "Docs", type: NewsType);
            Add("Install", "Guides");
            Add("Upgrade", "Guides");
            Add("Deep", "Upgrade");
            Add("Secret", "Docs", hidden: true);
            Add("Under secret", "Secret");
            Add("Draft", "Docs", published: false);
            Add("Under draft", "Draft");
        }

        void Add(string name, string? parent, string? description = null, Guid? type = null, bool hidden = false, bool published = true)
        {
            var key = Guid.NewGuid();
            Keys[name] = key;
            pages[key] = published ? new PageInfo(key, name, $"https://x.dk/{name.ToLower().Replace(' ', '-')}/", description, type ?? PageType, hidden) : null;
            kids[key] = [];
            if (parent is not null) kids[Keys[parent]].Add(key);
        }

        public Task<string> Render(params LlmsBlock[] blocks) => LlmsWriter.RenderAsync(
            new LlmsConfig { Title = "Site", Blocks = [.. blocks] },
            k => Task.FromResult(pages.GetValueOrDefault(k)), k => kids.GetValueOrDefault(k) ?? []);
    }

    static string[] Links(string text) => text.Split('\n').Where(l => l.StartsWith("- [")).Select(l => l[3..l.IndexOf(']')]).ToArray();

    [Fact]
    public async Task Header_text_heading_and_links_follow_llmstxt_format()
    {
        var t = new Tree();
        var text = await LlmsWriter.RenderAsync(new LlmsConfig
        {
            Title = "Acme", Summary = "We make\nthings.",
            Blocks =
            [
                new() { Type = BlockType.Text, Text = "Some *markdown*." },
                new() { Type = BlockType.Heading, Text = "Docs" },
                new() { Type = BlockType.Link, Text = "API [v2]", Url = "https://api.acme.dk/", Description = "Reference" },
                new() { Type = BlockType.Link, Url = "https://acme.dk/a b" , Text = "Spaces" },
            ],
        }, _ => Task.FromResult<PageInfo?>(null), _ => []);

        Assert.Equal(
            "# Acme\n\n> We make things.\n\nSome *markdown*.\n\n## Docs\n\n- [API \\[v2\\]](https://api.acme.dk/): Reference\n- [Spaces](https://acme.dk/a%20b)\n",
            text);
    }

    [Fact]
    public async Task Section_without_descendants_lists_published_visible_children()
    {
        var t = new Tree();
        var text = await t.Render(new LlmsBlock { Type = BlockType.Section, Node = t.Keys["Docs"], Text = "Docs" });
        Assert.Contains("## Docs\n\n- [Intro]", text);
        Assert.Equal(["Intro", "Guides"], Links(text));
        Assert.Contains("- [Intro](https://x.dk/intro/): Start here now", text);
    }

    [Fact]
    public async Task Descendants_are_depth_first_and_respect_max_depth()
    {
        var t = new Tree();
        var all = await t.Render(new LlmsBlock { Type = BlockType.Section, Node = t.Keys["Docs"], IncludeDescendants = true });
        Assert.Equal(["Intro", "Guides", "Install", "Upgrade", "Deep"], Links(all));

        var two = await t.Render(new LlmsBlock { Type = BlockType.Section, Node = t.Keys["Docs"], IncludeDescendants = true, MaxDepth = 2 });
        Assert.Equal(["Intro", "Guides", "Install", "Upgrade"], Links(two));
    }

    [Fact]
    public async Task Hidden_pages_are_listed_only_when_not_skipped()
    {
        var t = new Tree();
        var text = await t.Render(new LlmsBlock { Type = BlockType.Section, Node = t.Keys["Docs"], IncludeDescendants = true, SkipHidden = false });
        Assert.Equal(["Intro", "Guides", "Install", "Upgrade", "Deep", "Secret", "Under secret"], Links(text));
    }

    [Fact]
    public async Task Type_filters_skip_the_page_but_keep_its_children()
    {
        var t = new Tree();
        var excluded = await t.Render(new LlmsBlock { Type = BlockType.Section, Node = t.Keys["Docs"], IncludeDescendants = true, ExcludeTypes = [NewsType] });
        Assert.Equal(["Intro", "Install", "Upgrade", "Deep"], Links(excluded));

        var included = await t.Render(new LlmsBlock { Type = BlockType.Section, Node = t.Keys["Docs"], IncludeDescendants = true, IncludeTypes = [NewsType] });
        Assert.Equal(["Guides"], Links(included));
    }

    [Fact]
    public async Task Max_pages_stops_the_list()
    {
        var t = new Tree();
        var text = await t.Render(new LlmsBlock { Type = BlockType.Section, Node = t.Keys["Docs"], IncludeDescendants = true, MaxPages = 3 });
        Assert.Equal(["Intro", "Guides", "Install"], Links(text));
    }

    [Fact]
    public async Task Node_links_fall_back_to_page_name_and_description_and_skip_unpublished()
    {
        var t = new Tree();
        var text = await t.Render(
            new LlmsBlock { Type = BlockType.Link, Node = t.Keys["Intro"] },
            new LlmsBlock { Type = BlockType.Link, Node = t.Keys["Intro"], Text = "Begin", Description = "Own" },
            new LlmsBlock { Type = BlockType.Link, Node = t.Keys["Draft"] });
        Assert.Contains("- [Intro](https://x.dk/intro/): Start here now\n- [Begin](https://x.dk/intro/): Own\n", text);
        Assert.Equal(2, Links(text).Length);
    }

    [Fact]
    public void Validation_reports_per_block_and_trims()
    {
        var c = new LlmsConfig
        {
            Title = "  ",
            DescriptionAliases = [" metaDescription ", "", "METADESCRIPTION"],
            Blocks =
            [
                new() { Type = BlockType.Heading },
                new() { Type = BlockType.Link, Url = "javascript:alert(1)" },
                new() { Type = BlockType.Section },
                new() { Type = BlockType.Section, Node = Guid.NewGuid(), MaxDepth = 0 },
                new() { Type = "nope" },
                new() { Type = BlockType.Link, Url = " https://ok.dk ", Text = "Ok" },
            ],
        };
        var errors = LlmsWriter.Validate(c);

        Assert.Equal(
            [new("laitLlms_errorTitle"), new("laitLlms_errorText", 1), new("laitLlms_errorUrl", 2), new("laitLlms_errorLinkTitle", 2),
             new("laitLlms_errorNode", 3), new("laitLlms_errorLimits", 4), new LlmsError("laitLlms_errorType", 5)],
            errors);
        Assert.Equal(["metaDescription"], c.DescriptionAliases);
        Assert.Equal("https://ok.dk", c.Blocks[5].Url);
    }
}
