using Asp.Versioning;
using Lait.Umbraco.LLMS.Generator.Composing;
using Lait.Umbraco.LLMS.Generator.Generating;
using Lait.Umbraco.LLMS.Generator.Models;
using Lait.Umbraco.LLMS.Generator.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Cms.Web.Common.Routing;

namespace Lait.Umbraco.LLMS.Generator.Controllers;

/// <summary>Serves /llms.txt for the requested host.</summary>
public class LlmsTxtController(LlmsService llms) : ControllerBase
{
    [HttpGet("/llms.txt")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> Get() =>
        await llms.ForRequestAsync(new Uri(Request.GetDisplayUrl())) is { } text
            ? Content(text, "text/plain; charset=utf-8")
            : NotFound();
}

/// <summary>Dashboard API under /umbraco/lait-llms/api/v1/ (Umbraco's documented pattern for package APIs).</summary>
[ApiController]
[BackOfficeRoute("lait-llms/api/v{version:apiVersion}")]
[MapToApi(LlmsApi.Name)]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Lait LLMs")]
[Authorize(Policy = AuthorizationPolicies.SectionAccessContent)]
public class LlmsController(LlmsService llms, LlmsStore store, IContentService contentService) : ControllerBase
{
    [HttpGet("sites")]
    [ProducesResponseType(typeof(IEnumerable<SiteInfo>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Sites() => Ok(await llms.SitesAsync());

    /// <summary>The saved config, or a starting point titled after the site node.</summary>
    [HttpGet("config")]
    [ProducesResponseType(typeof(LlmsConfig), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid root)
    {
        if (!await IsSiteAsync(root)) return NotFound();
        return Ok(store.Get(root) ?? new LlmsConfig { Title = contentService.GetById(root)?.Name ?? "" });
    }

    [HttpPut("config")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IEnumerable<LlmsError>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Save(Guid root, LlmsConfig config)
    {
        if (!await IsSiteAsync(root)) return NotFound();
        var errors = LlmsWriter.Validate(config);
        if (errors.Count > 0) return BadRequest(errors);
        store.Save(root, config);
        llms.ClearCache();
        return Ok();
    }

    /// <summary>The file the posted (unsaved) config would produce. Not validated: the preview follows the form as it is typed.</summary>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(Guid root, LlmsConfig config)
    {
        if (!await IsSiteAsync(root)) return NotFound();
        config.Blocks ??= [];
        foreach (var b in config.Blocks) { b.IncludeTypes ??= []; b.ExcludeTypes ??= []; }
        config.DescriptionAliases ??= [];
        config.HiddenAliases ??= [];
        return Content(await llms.PreviewAsync(root, config, new Uri(Request.GetDisplayUrl())), "text/plain; charset=utf-8");
    }

    async Task<bool> IsSiteAsync(Guid root) => (await llms.SitesAsync()).Any(s => s.Key == root);
}
