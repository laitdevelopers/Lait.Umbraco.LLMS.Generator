namespace Lait.Umbraco.LLMS.Generator.Models;

/// <summary>What the dashboard edits for one site root. Stored as JSON, one row per root node.</summary>
public sealed class LlmsConfig
{
    /// <summary>The "# Title" line. Required by llmstxt.org.</summary>
    public string Title { get; set; } = "";
    /// <summary>The "> summary" line. Optional.</summary>
    public string Summary { get; set; } = "";
    /// <summary>Property aliases tried in order for a page's description; the first with a value wins.</summary>
    public List<string> DescriptionAliases { get; set; } = ["metaDescription", "description"];
    /// <summary>Property aliases that hide a page (and everything under it) from sections when true.</summary>
    public List<string> HiddenAliases { get; set; } = ["umbracoNaviHide"];
    public List<LlmsBlock> Blocks { get; set; } = [];
}

public static class BlockType
{
    public const string Text = "text";
    public const string Heading = "heading";
    public const string Link = "link";
    public const string Section = "section";
    public static readonly string[] All = [Text, Heading, Link, Section];
}

/// <summary>One block. Which fields are used depends on <see cref="Type"/>.</summary>
public sealed class LlmsBlock
{
    /// <summary>Client-side id, only used to keep drag-and-drop stable.</summary>
    public string Id { get; set; } = "";
    public string Type { get; set; } = BlockType.Text;
    /// <summary>Text: markdown. Heading: the heading. Link: title (empty = page name). Section: "## heading" above the list (empty = none).</summary>
    public string Text { get; set; } = "";

    /// <summary>Link to an Umbraco page, or the node whose pages a section lists.</summary>
    public Guid? Node { get; set; }
    /// <summary>External link (used when <see cref="Node"/> is empty).</summary>
    public string Url { get; set; } = "";
    /// <summary>Link description (empty = from the page's description properties).</summary>
    public string Description { get; set; } = "";

    // Section only.
    public bool IncludeDescendants { get; set; }
    /// <summary>Levels below the section node; 1 = children only. Null = no limit.</summary>
    public int? MaxDepth { get; set; }
    public int? MaxPages { get; set; }
    /// <summary>Document type keys to list (empty = all). Pages of other types are not listed, but their children still are.</summary>
    public List<Guid> IncludeTypes { get; set; } = [];
    /// <summary>Document type keys not to list. Their children still are.</summary>
    public List<Guid> ExcludeTypes { get; set; } = [];
    public bool SkipHidden { get; set; } = true;
}

/// <summary>A validation problem: a localization key and the 1-based block it is about (null = the whole file).</summary>
public sealed record LlmsError(string Term, int? Block = null);
