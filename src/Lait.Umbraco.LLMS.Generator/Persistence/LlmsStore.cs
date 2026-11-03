using System.Text.Json;
using Lait.Umbraco.LLMS.Generator.Models;
using NPoco;
using Umbraco.Cms.Core.Packaging;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Lait.Umbraco.LLMS.Generator.Persistence;

[TableName("laitLlmsSite")]
[PrimaryKey("Id", AutoIncrement = true)]
[ExplicitColumns]
public class SiteRow
{
    [Column("Id")][PrimaryKeyColumn(AutoIncrement = true)] public int Id { get; set; }
    /// <summary>Key of the root content node the config belongs to.</summary>
    [Column("RootKey")][Index(IndexTypes.UniqueNonClustered)] public Guid RootKey { get; set; }
    /// <summary>JSON <see cref="LlmsConfig"/>.</summary>
    [Column("ConfigJson")][SpecialDbType(SpecialDbTypes.NVARCHARMAX)] public string ConfigJson { get; set; } = "";
    [Column("UpdatedUtc")] public DateTime UpdatedUtc { get; set; }
}

public class CreateLlmsTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!TableExists("laitLlmsSite")) Create.Table<SiteRow>().Do();
        return Task.CompletedTask;
    }
}

/// <summary>Umbraco runs this unattended on startup (SQL Server and SQLite).</summary>
public class LlmsMigrationPlan() : PackageMigrationPlan("Lait.Umbraco.LLMS.Generator")
{
    protected override void DefinePlan() => To<CreateLlmsTables>(new Guid("3b8e1f62-9c4d-4a17-b2e5-6d0f7a9c1e01"));
}

public sealed class LlmsStore(IScopeProvider scopeProvider)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The saved config, or null when the site has none (then /llms.txt is 404).</summary>
    public LlmsConfig? Get(Guid rootKey)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var row = scope.Database.SingleOrDefault<SiteRow>("WHERE RootKey = @0", rootKey);
        return row is null ? null : JsonSerializer.Deserialize<LlmsConfig>(row.ConfigJson, Json);
    }

    public HashSet<Guid> ConfiguredRoots()
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        return scope.Database.Fetch<Guid>("SELECT RootKey FROM laitLlmsSite").ToHashSet();
    }

    public void Save(Guid rootKey, LlmsConfig config)
    {
        using var scope = scopeProvider.CreateScope();
        var row = scope.Database.SingleOrDefault<SiteRow>("WHERE RootKey = @0", rootKey) ?? new SiteRow { RootKey = rootKey };
        row.ConfigJson = JsonSerializer.Serialize(config, Json);
        row.UpdatedUtc = DateTime.UtcNow;
        if (row.Id == 0) scope.Database.Insert(row); else scope.Database.Update(row);
        scope.Complete();
    }
}
