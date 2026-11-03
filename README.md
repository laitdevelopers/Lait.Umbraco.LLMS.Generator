# LLMs.txt Generator for Umbraco

Build your **`/llms.txt`** from the Umbraco 17 backoffice. `llms.txt` is a markdown file at the root of a site that
tells AI assistants what the site is and which pages matter ([llmstxt.org](https://llmstxt.org)).

Editors compose the file from blocks in **Content → llms.txt**:

- **Text**: markdown, written into the file as it is.
- **Heading**: starts a `## ` section; the links below it belong to it.
- **Link**: one link to a page in Umbraco or an external URL, with title and description.
- **Section**: lists the pages under a node, as children only or with all descendants. Filter by max levels,
  max pages, document types (only these / not these) and hidden pages. Kept up to date when content is published.

Blocks are reordered by drag and drop, and a live preview shows the file while you edit. Saving publishes it.

```markdown
# Acme

> Acme makes rockets for coyotes.

## Docs

- [Install](https://acme.com/docs/install/): How to install
- [Upgrade](https://acme.com/docs/upgrade/)
- [Source code](https://github.com/acme)
```

## Install

```bash
dotnet add package Lait.Umbraco.LLMS.Generator
```

Requires Umbraco 17 (.NET 10). The database table is created automatically on startup (SQL Server and SQLite).
Then open **Content → llms.txt**, add blocks and press **Save and publish**. Everyone with access to the Content
section can edit it. The dashboard is in English and Danish (follows the user's backoffice language).

## How it works

- **One file per site.** Each node with a domain gets its own `llms.txt`, served on that domain's host. Without any
  domains, the first root node is served on every host. Until a site is saved, its `/llms.txt` is a 404.
- **Language follows the domain.** Page names, URLs and descriptions come from the domain's culture.
- **Descriptions** for Umbraco pages come from the first property with a value in *Page properties → Description
  properties* (default `metaDescription, description`). HTML is stripped. A description typed in a link block wins.
- **Hidden pages** are those where a property in *Hidden properties* (default `umbracoNaviHide`) is true. With
  *Skip hidden pages* on, a hidden page and everything under it is left out of a section.
- **Document type filters** only decide whether a page is listed: the pages under a filtered-out page are still listed.
- Only published, routable pages are listed. A section lists at most 1,000 pages.
- The file is generated on request and cached. The cache is cleared when content or domains change (on all servers)
  and when the configuration is saved (on the server that saved it; other servers pick it up within an hour).

## Develop

```bash
dotnet test
```

- `test/setup-local-test.ps1` creates a throwaway Umbraco 17 site (SQLite) that references the project.
- `test/pack-local.ps1` packs to a local NuGet folder feed so any site on your machine can `dotnet add package` it.

Release: bump `<Version>` in the `.csproj` and `version` in `wwwroot/umbraco-package.json` (plus the `?v=` cache
busters in it), then pack and push with your nuget.org API key:

```bash
dotnet pack src/Lait.Umbraco.LLMS.Generator -c Release -o artifacts
dotnet nuget push artifacts/Lait.Umbraco.LLMS.Generator.<version>.nupkg --api-key <key> --source https://api.nuget.org/v3/index.json
```

The Umbraco Marketplace picks it up from NuGet and reads `umbraco-marketplace.json` from this repository.

## Author

Made by [LAIT](https://lait.dk), Anders Bootsmann Overvad.

## License

MIT
