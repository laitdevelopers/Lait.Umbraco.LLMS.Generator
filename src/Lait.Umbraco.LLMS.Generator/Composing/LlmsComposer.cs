using Lait.Umbraco.LLMS.Generator.Generating;
using Lait.Umbraco.LLMS.Generator.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbraco.Cms.Api.Management.OpenApi;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Lait.Umbraco.LLMS.Generator.Composing;

public static class LlmsApi
{
    public const string Name = "lait-llms";
}

public sealed class LlmsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        // MVC does not discover controllers in this class library on its own (not in the app's dependency context), so register it.
        builder.Services.AddControllers().AddApplicationPart(typeof(LlmsComposer).Assembly);

        // Own Swagger document so the API is grouped and gets Umbraco's backoffice auth in Swagger UI.
        builder.Services.Configure<SwaggerGenOptions>(o =>
        {
            o.SwaggerDoc(LlmsApi.Name, new OpenApiInfo { Title = "Lait LLMs.txt API", Version = "1.0" });
            o.OperationFilter<LlmsOperationSecurityFilter>();
        });

        builder.Services.AddSingleton<LlmsStore>();
        builder.Services.AddSingleton<LlmsService>();

        // Cache refresher notifications fire on every server (load balancing) for publish, unpublish, move, delete and domain changes.
        builder.AddNotificationHandler<ContentCacheRefresherNotification, ClearLlmsCache>();
        builder.AddNotificationHandler<DomainCacheRefresherNotification, ClearLlmsCache>();
    }
}

public sealed class ClearLlmsCache(LlmsService llms) :
    INotificationHandler<ContentCacheRefresherNotification>, INotificationHandler<DomainCacheRefresherNotification>
{
    public void Handle(ContentCacheRefresherNotification notification) => llms.ClearCache();
    public void Handle(DomainCacheRefresherNotification notification) => llms.ClearCache();
}

public sealed class LlmsOperationSecurityFilter : BackOfficeSecurityRequirementsOperationFilterBase
{
    protected override string ApiName => LlmsApi.Name;
}
