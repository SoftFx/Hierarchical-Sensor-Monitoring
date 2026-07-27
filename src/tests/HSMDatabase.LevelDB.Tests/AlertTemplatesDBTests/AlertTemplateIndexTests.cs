using System;
using System.Linq;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Tests.DatabaseTests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.AlertTemplatesDBTests;

[Collection("Database collection")]
public class AlertTemplateIndexTests : DatabaseCoreTestsBase<AlertTemplateIndexFixture>, IClassFixture<DatabaseRegisterFixture>
{
    public AlertTemplateIndexTests(AlertTemplateIndexFixture fixture, DatabaseRegisterFixture registerFixture)
        : base(fixture, registerFixture) { }


    // Regression for #1312: AddAlertTemplateIdToList used List<byte[]>.Contains, which compares arrays
    // by reference. Every re-save of a template appended another copy of its GUID to the "AlertTemplates"
    // index. Repeated AddAlertTemplate with the same Id must keep the index at a single entry.
    [Fact]
    public void AddAlertTemplate_SameIdRepeated_KeepsSingleIndexEntry()
    {
        var id = Guid.NewGuid();
        var entity = BuildEntity(id);

        _databaseCoreManager.DatabaseCore.AddAlertTemplate(entity);
        _databaseCoreManager.DatabaseCore.AddAlertTemplate(entity);
        _databaseCoreManager.DatabaseCore.AddAlertTemplate(entity);

        var all = _databaseCoreManager.DatabaseCore.GetAllAlertTemplates();

        var matching = all.Where(t => new Guid(t.Id) == id).ToList();
        Assert.Single(matching);
    }

    [Fact]
    public void AddAlertTemplate_DistinctIds_KeepsAllEntries()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        _databaseCoreManager.DatabaseCore.AddAlertTemplate(BuildEntity(idA));
        _databaseCoreManager.DatabaseCore.AddAlertTemplate(BuildEntity(idB));

        var all = _databaseCoreManager.DatabaseCore.GetAllAlertTemplates();

        Assert.Contains(all, t => new Guid(t.Id) == idA);
        Assert.Contains(all, t => new Guid(t.Id) == idB);
    }


    private static AlertTemplateEntity BuildEntity(Guid id) => new()
    {
        Id = id.ToByteArray(),
        Name = $"Template {id}",
        FolderId = Guid.Empty,
        SensorType = 0,
        Policies = [],
        TTLPolicies = [],
        TTLs = [],
        Paths = [],
    };
}

public class AlertTemplateIndexFixture : DatabaseFixture
{
    protected override string DatabaseFolder => nameof(AlertTemplateIndexTests);
}
