using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.TreeStateSnapshot;
using HSMServer.Core.TreeStateSnapshot.States;
using HSMTableBlocks;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    /// <summary>
    /// The table sensor kind (ADR-0021, #1844): registration with a schema, what it refuses, and how a tree with a
    /// sensor entity this build cannot build still loads (the rollback rule).
    /// </summary>
    // The fixture database is shared by every test in the class, so each test works under its own node name.
    [Collection("Database collection")]
    public class TableSensorTests : MonitoringCoreTestsBase<TableSensorFixture>
    {
        private readonly TableSensorFixture _fixture;

        private static readonly TableSchema Quotes = Schema(("time", TableColumnType.Time), ("bid", TableColumnType.Double), ("ask", TableColumnType.Double));


        public TableSensorTests(TableSensorFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        [Fact]
        public async Task Registration_CreatesATableSensor_WithItsSchema_AndPersistsIt()
        {
            _valuesCache.TableBlockSequence = () => 4242;
            var path = $"{Unique()}/quotes";

            var result = await RegisterTableAsync(path, Quotes);

            Assert.True(result.IsOk, result.Error);
            var sensor = Assert.IsType<TableSensorModel>(GetSensor(path));
            Assert.Equal(SensorType.Table, sensor.Type);
            Assert.True(sensor.Schema.SameAs(Quotes));
            Assert.Equal(4242, sensor.FirstSequence);
            Assert.Null(sensor.LastValue);
            Assert.False(sensor.HasData);

            var entity = StoredEntity(sensor.Id);
            Assert.Equal((byte)SensorType.Table, entity.Type);
            Assert.Equal(Quotes.Id, entity.Table.SchemaId);
            Assert.Equal(4242, entity.Table.FirstSequence);
            Assert.Equal(["time", "bid", "ask"], entity.Table.Columns.Select(c => c.Name));
            Assert.Equal([(byte)1, (byte)3, (byte)3], entity.Table.Columns.Select(c => c.Type));

            // A restart builds the same sensor from the entity.
            await using var restarted = BuildCacheOverTheSameDatabase();
            Assert.True(restarted.TryGetSensorByPath(_fixture.Product, path, out var reloaded));
            var table = Assert.IsType<TableSensorModel>(reloaded);
            Assert.True(table.Schema.SameAs(Quotes));
            Assert.Equal(4242, table.FirstSequence);
        }

        [Fact]
        public async Task Registration_Again_WithTheSameSchema_IsOk_AndWithAnotherSchema_IsRefused()
        {
            var path = $"{Unique()}/quotes";
            Assert.True((await RegisterTableAsync(path, Quotes)).IsOk);
            var sensor = GetSensor(path);

            var again = await RegisterTableAsync(path, Schema(("time", TableColumnType.Time), ("bid", TableColumnType.Double), ("ask", TableColumnType.Double)), "described");
            Assert.True(again.IsOk, again.Error);
            Assert.Equal("described", sensor.Description);

            foreach (var other in new[]
            {
                Schema(("time", TableColumnType.Time), ("bid", TableColumnType.Double)),
                Schema(("time", TableColumnType.Time), ("bid", TableColumnType.Double), ("ask", TableColumnType.Int64)),
                Schema(("time", TableColumnType.Time), ("bid", TableColumnType.Double), ("Ask", TableColumnType.Double)),
            })
            {
                var refused = await RegisterTableAsync(path, other, "changed");

                AssertRefusal(refused);
                Assert.Contains("is registered with the schema (time Time, bid Double, ask Double)", refused.Error);
                Assert.Contains("is refused", refused.Error);
            }

            Assert.Same(sensor, GetSensor(path));
            Assert.True(((TableSensorModel)sensor).Schema.SameAs(Quotes));
            Assert.Equal("described", sensor.Description);
            Assert.Equal(Quotes.Id, StoredEntity(sensor.Id).Table.SchemaId);
        }

        [Fact]
        public async Task Registration_IsRefused_OnAPathOfAnotherKind_WithoutASchema_AndAsAnotherKindOnATablePath()
        {
            var root = Unique();
            var integer = await CreateIntSensorAsync($"{root}/int");

            var onInteger = await RegisterTableAsync($"{root}/int", Quotes);
            AssertRefusal(onInteger);
            Assert.Contains("cannot become a table sensor", onInteger.Error);
            Assert.Same(integer, GetSensor($"{root}/int"));

            var noSchema = await RegisterTableAsync($"{root}/none", null);
            AssertRefusal(noSchema);
            Assert.Contains("needs a schema", noSchema.Error);
            Assert.False(_valuesCache.TryGetSensorByPath(_fixture.Product, $"{root}/none", out _));

            Assert.True((await RegisterTableAsync($"{root}/table", Quotes)).IsOk);
            var asDouble = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(_fixture.Product, $"{root}/table")
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.Double,
                TypeIsExplicit = true,
            });
            AssertRefusal(asDouble);
            Assert.Contains("is a table sensor", asDouble.Error);
            Assert.IsType<TableSensorModel>(GetSensor($"{root}/table"));
        }

        [Fact]
        public async Task A_value_addressed_to_a_table_sensor_is_rejected()
        {
            var path = $"{Unique()}/quotes";
            Assert.True((await RegisterTableAsync(path, Quotes)).IsOk);
            var sensor = GetSensor(path);

            foreach (var type in new[] { SensorType.Integer, SensorType.Double, SensorType.String })
            {
                var value = SensorValuesFactory.BuildSensorValue(type, path, DateTime.UtcNow);
                await _valuesCache.AddSensorValueAsync(_fixture.Key, _fixture.Product, value);
            }

            Assert.Same(sensor, GetSensor(path));
            Assert.Null(sensor.LastValue);
            Assert.False(sensor.HasData);
            Assert.Equal(0, await CountHistoryRowsAsync(sensor.Id));
            Assert.False(sensor.TryAddValue(new IntegerValue { Value = 1, Time = DateTime.UtcNow }));
            Assert.Throws<NotSupportedException>(() => sensor.GetEmptyValue());

            var edit = await _valuesCache.UpdateSensorValueAsync(new UpdateSensorValueRequestModel(sensor.Id, path)
            {
                Id = sensor.Id,
                Comment = "edited",
                Value = "1",
                Initiator = InitiatorInfo.System,
            });
            AssertRefusal(edit);
            Assert.Contains("table sensor", edit.Error);
            Assert.Null(sensor.LastValue);
        }

        [Fact]
        public async Task A_table_sensor_takes_no_alerts_no_ttl_and_no_aggregation()
        {
            var root = Unique();
            var integer = await CreateIntSensorAsync($"{root}/int");

            // The node gets a TTL policy: a sensor created under it inherits it, a table sensor does not.
            var nodeTtl = await _valuesCache.UpdateProductAsync(new ProductUpdate
            {
                Id = integer.Parent.Id,
                TTLPolicies = [TtlUpdate(TimeSpan.FromMinutes(30))],
                Initiator = InitiatorInfo.System,
            }, default);
            Assert.True(nodeTtl.IsOk, nodeTtl.Error);

            Assert.True((await RegisterTableAsync($"{root}/table", Quotes)).IsOk);
            var table = GetSensor($"{root}/table");
            var sibling = await CreateIntSensorAsync($"{root}/int2");

            Assert.NotEmpty(sibling.Policies.TTLPolicies);
            Assert.Empty(table.Policies.TTLPolicies);
            Assert.Empty(table.Policies);
            Assert.False(table.CheckTimeout());

            foreach (var update in new[]
            {
                new SensorUpdate { Id = table.Id, TTLPolicies = [TtlUpdate(TimeSpan.FromMinutes(5))], Initiator = InitiatorInfo.System },
                new SensorUpdate { Id = table.Id, Policies = [DataPolicy()], Initiator = InitiatorInfo.System },
                new SensorUpdate { Id = table.Id, AggregateValues = true, Initiator = InitiatorInfo.System },
            })
            {
                var result = await _valuesCache.UpdateSensorAsync(update);

                AssertRefusal(result);
                Assert.Contains("is a table sensor", result.Error);
            }

            Assert.Empty(table.Policies.TTLPolicies);
            Assert.Empty(table.Policies);
            Assert.False(table.AggregateValues);

            // A description is fine.
            Assert.True((await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = table.Id, Description = "ticks" })).IsOk);
            Assert.Equal("ticks", table.Description);
        }

        [Fact]
        public async Task No_alert_template_matches_a_table_sensor()
        {
            var path = $"{Unique()}/quotes";
            Assert.True((await RegisterTableAsync(path, Quotes)).IsOk);
            var sensor = GetSensor(path);

            foreach (var type in new[] { AlertTemplateModel.AnyType, (byte)SensorType.Table })
            {
                var template = new AlertTemplateModel
                {
                    Name = $"Table template {Guid.NewGuid():N}",
                    FolderId = _fixture.Folder,
                    SensorType = type,
                    Paths = [$"{TableSensorFixture.ProductName}/**"],
                };
                Assert.True(template.TryApplyPathTemplates(out var errors), errors);

                Assert.False(template.IsMatch(sensor));
            }
        }

        [Fact]
        public async Task A_table_sensor_never_moves()
        {
            var root = Unique();
            Assert.True((await RegisterTableAsync($"{root}/quotes", Quotes)).IsOk);
            var table = GetSensor($"{root}/quotes");
            var integer = await CreateIntSensorAsync($"{root}/int");

            // A move, a rename, and a move onto a free path in another node.
            foreach (var destination in new[] { $"{root}/renamed", $"{root}/other/quotes", $"{Unique()}/quotes" })
            {
                var moved = await _valuesCache.MoveSensorAsync(table.Id, _fixture.Product, destination);
                Assert.Equal(SensorMoveOutcome.NotSupported, moved.Outcome);
                Assert.Contains($"{root}/quotes", moved.Error);
                Assert.Contains("table sensor", moved.Error);

                var preview = _valuesCache.PreviewMove(table.Id, _fixture.Product, destination);
                Assert.Equal(SensorMoveOutcome.NotSupported, preview.Outcome);
            }

            // A merge in either direction.
            Assert.True((await RegisterTableAsync($"{root}/quotes2", Quotes)).IsOk);
            Assert.Equal(SensorMoveOutcome.NotSupported,
                (await _valuesCache.MoveSensorAsync(table.Id, _fixture.Product, $"{root}/quotes2", SensorMoveConflict.Merge)).Outcome);
            var onto = await _valuesCache.MoveSensorAsync(integer.Id, _fixture.Product, $"{root}/quotes", SensorMoveConflict.Merge);
            Assert.Equal(SensorMoveOutcome.Conflict, onto.Outcome);
            Assert.Contains("Table", onto.Error);

            // A collector that lists the table's path as a previous path of a new one.
            var previous = await _valuesCache.ApplyPreviousPathsAsync(_fixture.Key, _fixture.Product, $"{root}/quotes3", [$"{root}/quotes"], SensorType.Table, InitiatorInfo.System);
            Assert.True(previous.IsOk);

            Assert.Same(table, GetSensor($"{root}/quotes"));
            Assert.Equal($"{root}/quotes", table.Path);
            Assert.Empty(table.Aliases);
            Assert.Same(integer, GetSensor($"{root}/int"));
            Assert.False(_valuesCache.TryGetSensorByPath(_fixture.Product, $"{root}/renamed", out _));
        }

        [Fact]
        public async Task A_node_above_a_table_sensor_is_not_renamed()
        {
            var root = Unique();
            Assert.True((await RegisterTableAsync($"{root}/inner/quotes", Quotes)).IsOk);
            var table = GetSensor($"{root}/inner/quotes");
            var inner = table.Parent;
            var outer = inner.Parent;

            foreach (var node in new[] { inner, outer })
            {
                var result = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = node.Id, Name = "renamed" }, default);

                // No node below a root is renamed (#1881), so the node above a table sensor is refused by
                // that rule, which names the node, not the table sensor.
                AssertRefusal(result);
                Assert.Contains(node.FullPath, result.Error);
                Assert.Contains("only a root product can be renamed", result.Error);
            }

            Assert.Equal("inner", inner.DisplayName);
            Assert.Equal($"{root}/inner/quotes", table.Path);
        }

        [Fact]
        public async Task History_clear_and_retention_skip_a_table_sensor()
        {
            var root = Unique();
            Assert.True((await RegisterTableAsync($"{root}/quotes", Quotes)).IsOk);
            var table = GetSensor($"{root}/quotes");
            var integer = await CreateIntSensorAsync($"{root}/int");

            var direct = await _valuesCache.ClearSensorHistoryAsync(new ClearHistoryRequest(table.Id));
            AssertRefusal(direct);
            Assert.Contains("not supported", direct.Error);

            // A node clear clears the other sensors and leaves the table sensor alone.
            await _valuesCache.ClearNodeHistoryAsync(new ClearHistoryRequest(table.Parent.Id));
            Assert.Equal(0, await CountHistoryRowsAsync(integer.Id));

            await _valuesCache.CheckSensorsHistoryAsync();
            Assert.Same(table, GetSensor($"{root}/quotes"));
        }

        [Fact]
        public async Task A_table_sensor_is_never_self_destroyed()
        {
            var path = $"{Unique()}/quotes";
            Assert.True((await RegisterTableAsync(path, Quotes)).IsOk);
            var table = GetSensor(path);

            var result = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = table.Id,
                SelfDestroy = new TimeIntervalModel(1),
            });
            Assert.True(result.IsOk, result.Error);

            Assert.True(table.SelfDestroyIsActive);
            Assert.False(table.ShouldDestroy());
        }

        [Fact]
        public async Task A_table_registration_that_carries_alerts_or_aggregation_is_refused_before_anything_is_created()
        {
            var root = Unique();

            foreach (var (name, update) in new[]
            {
                ("ttl", new SensorUpdate { Id = Guid.Empty, TTLPolicies = [TtlUpdate(TimeSpan.FromMinutes(5))] }),
                ("alert", new SensorUpdate { Id = Guid.Empty, Policies = [DataPolicy()] }),
                ("aggregate", new SensorUpdate { Id = Guid.Empty, AggregateValues = true }),
            })
            {
                var result = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(_fixture.Product, $"{root}/{name}")
                {
                    Update = update,
                    Type = SensorType.Table,
                    TypeIsExplicit = true,
                    Schema = Quotes,
                });

                AssertRefusal(result);
                Assert.Contains("is a table sensor", result.Error);
                Assert.False(_valuesCache.TryGetSensorByPath(_fixture.Product, $"{root}/{name}", out _), $"{name}: a sensor was created");
            }

            // The same for a registration without a type on an existing table sensor.
            Assert.True((await RegisterTableAsync($"{root}/table", Quotes)).IsOk);
            var untyped = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(_fixture.Product, $"{root}/table")
            {
                Update = new SensorUpdate { Id = Guid.Empty, Policies = [DataPolicy()] },
                Type = SensorType.Boolean,
                TypeIsExplicit = false,
            });
            AssertRefusal(untyped);
            Assert.Empty(GetSensor($"{root}/table").Policies);
        }

        [Fact]
        public async Task A_store_fault_during_a_table_registration_is_still_a_fault()
        {
            // The other side of the refusals (#1756): a write the store failed on can pass on a repeat,
            // so it stays a fault, which the transports answer 500 and the collector sends again.
            var path = $"{Unique()}/quotes";
            var failing = new FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailSensorAdd = e => e.Type == (byte)SensorType.Table,
            };
            await using var cache = BuildCacheOverTheSameDatabase(failing);

            var result = await cache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(_fixture.Product, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.Table,
                TypeIsExplicit = true,
                Schema = Quotes,
            });

            Assert.False(result.IsOk);
            Assert.True(result.IsFault, result.Error);
            Assert.False(cache.TryGetSensorByPath(_fixture.Product, path, out _));
        }

        [Fact]
        public async Task A_sensor_entity_this_build_cannot_build_is_skipped_and_left_as_stored()
        {
            // What an older server does with a table sensor's entity (type 11 and the Table field it does not know):
            // its factory throws for the unknown type, the tree load catches that per sensor and goes on, and nothing
            // writes the entity back. This build knows 11, so the same path is driven with a type it does not know
            // (12), and with type 11 whose schema does not hold.
            var root = Unique();
            var integer = await CreateIntSensorAsync($"{root}/int");
            var parent = integer.Parent.Id.ToString();

            var unknown = Entity(parent, "future", type: 12, Quotes, firstSequence: 7);
            var badSchema = Entity(parent, "badschema", type: (byte)SensorType.Table, Quotes, firstSequence: 7) with
            {
                Table = TableSensorModel.ToEntity(Quotes, 7) with { SchemaId = Quotes.Id ^ 1 },
            };
            var noSchema = Entity(parent, "noschema", type: (byte)SensorType.Table, null, firstSequence: 0);

            foreach (var entity in new[] { unknown, badSchema, noSchema })
                _databaseCoreManager.DatabaseCore.AddSensor(entity);

            var before = new[] { unknown, badSchema, noSchema }.Select(e => Json(StoredEntity(Guid.Parse(e.Id)))).ToList();

            await using var restarted = BuildCacheOverTheSameDatabase();

            Assert.True(restarted.TryGetSensorByPath(_fixture.Product, $"{root}/int", out var loaded));
            Assert.Equal(integer.Id, loaded.Id);

            foreach (var entity in new[] { unknown, badSchema, noSchema })
                Assert.Null(restarted.GetSensor(Guid.Parse(entity.Id)));

            // Read before the restarted cache is disposed: its disposal closes the shared database.
            var after = new[] { unknown, badSchema, noSchema }.Select(e => Json(StoredEntity(Guid.Parse(e.Id)))).ToList();
            Assert.Equal(before, after);
            Assert.Contains("\"Table\":{", before[0]);
        }

        [Fact]
        public void An_entity_with_a_member_this_build_does_not_know_still_deserializes()
        {
            // The other half of the rollback: an older server reads a newer entity with the default System.Text.Json
            // options of EnvironmentDatabaseWorker, which skip members they do not know (here a future one).
            var json = Json(Entity(Guid.NewGuid().ToString(), "x", (byte)SensorType.Table, Quotes, 3))
                .Replace("\"Table\":{", "\"FutureMember\":{\"A\":1},\"Table\":{");

            var entity = JsonSerializer.Deserialize<SensorEntity>(json);

            Assert.Equal((byte)SensorType.Table, entity.Type);
            Assert.Equal(Quotes.Id, entity.Table.SchemaId);
        }


        // A refusal: an error that a repeat cannot cure, so never a fault (a fault is answered 500 and the
        // collector sends it again on every cycle).
        private static void AssertRefusal(HSMCommon.TaskResult.TaskResult result)
        {
            Assert.False(result.IsOk);
            Assert.False(result.IsFault, $"a refusal reported as a fault: {result.Error}");
            Assert.False(string.IsNullOrEmpty(result.Error));
        }

        private static TableSchema Schema(params (string Name, TableColumnType Type)[] columns) =>
            new(columns.Select(c => new TableSchemaColumn(c.Name, c.Type)));

        private static string Unique() => $"t{Guid.NewGuid():N}"[..12];

        private static string Json(SensorEntity entity) => JsonSerializer.Serialize(entity);

        private static SensorEntity Entity(string parentId, string name, byte type, TableSchema schema, long firstSequence) => new()
        {
            Id = Guid.NewGuid().ToString(),
            ProductId = parentId,
            DisplayName = name,
            Type = type,
            CreationDate = DateTime.UtcNow.Ticks,
            Table = schema is null ? null : TableSensorModel.ToEntity(schema, firstSequence),
        };

        private Task<HSMCommon.TaskResult.TaskResult> RegisterTableAsync(string path, TableSchema schema, string description = null) =>
            _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(_fixture.Product, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty, Description = description },
                Type = SensorType.Table,
                TypeIsExplicit = true,
                Schema = schema,
            });

        private BaseSensorModel GetSensor(string path)
        {
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.Product, path, out var sensor), $"no sensor at {path}");
            return sensor;
        }

        private SensorEntity StoredEntity(Guid id) =>
            _databaseCoreManager.DatabaseCore.GetAllSensors().Single(e => e.Id == id.ToString());

        private async Task<BaseSensorModel> CreateIntSensorAsync(string path)
        {
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, path, DateTime.UtcNow);
            var result = await _valuesCache.AddSensorValueAsync(_fixture.Key, _fixture.Product, value);
            Assert.True(result.IsOk, result.Error);

            return GetSensor(path);
        }

        private async Task<int> CountHistoryRowsAsync(Guid sensorId)
        {
            var count = 0;

            await foreach (var _ in _databaseCoreManager.DatabaseCore.GetSensorValues(sensorId, DateTime.MinValue, DateTime.MaxValue))
                count++;

            return count;
        }

        private static PolicyUpdate TtlUpdate(TimeSpan interval) => new()
        {
            TTL = interval.Ticks,
            Initiator = InitiatorInfo.System,
            Destination = new PolicyDestinationUpdate(),
            Conditions = [],
        };

        private static PolicyUpdate DataPolicy() => new()
        {
            Id = Guid.NewGuid(),
            Template = "table",
            Destination = new PolicyDestinationUpdate(),
            Conditions =
            [
                new PolicyConditionUpdate(PolicyOperation.GreaterThan, PolicyProperty.Value, new TargetValue(TargetType.Const, "10")),
            ],
            Initiator = InitiatorInfo.System,
        };

        private TreeValuesCache BuildCacheOverTheSameDatabase(HSMServer.Core.DataLayer.IDatabaseCore database = null)
        {
            var snapshot = new Mock<ITreeStateSnapshot>();
            snapshot.Setup(a => a.Keys).Returns(new StateCollection<LastKeyState, LastKeyStateEntity>());

            database ??= _databaseCoreManager.DatabaseCore;

            return new TreeValuesCache(database, snapshot.Object, _journalService, _alertScheduleProvider,
                new HSMServer.Core.ChangeSets.ChangeSetJournal(database));
        }
    }
}
