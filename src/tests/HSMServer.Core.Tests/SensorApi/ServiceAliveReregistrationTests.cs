using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMServer.ApiObjectsConverters;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.SensorApi
{
    public sealed class ServiceAliveReregistrationFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(ServiceAliveReregistrationTests);
    }


    // What a collector's registration of ".module/Service alive" does to the TTL alert of a
    // sensor that already exists (#1666). Both collectors send the alert with its own 5-minute
    // inactivity period (TTLs [3000000000]); collectors before #1666 sent no interval
    // (TTLs [null]), which the server resolves through the sensor's parents (None on a product
    // with default settings, so that alert never fired).
    //
    // The registration goes through the real conversion of the Sensor API
    // (ApiConverters.Convert) and the cache's registration path, as on the wire.
    public class ServiceAliveReregistrationTests : MonitoringCoreTestsBase<ServiceAliveReregistrationFixture>
    {
        private static readonly long FiveMinutes = TimeSpan.FromMinutes(5).Ticks;
        private static readonly long ThirtyMinutes = TimeSpan.FromMinutes(30).Ticks;

        private static readonly InitiatorInfo Operator = InitiatorInfo.AsUser("operator");


        public ServiceAliveReregistrationTests(ServiceAliveReregistrationFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture) { }


        // The Service alive TTL alert as both collectors send it (WireFormatGoldenLockTests,
        // native default service alive wire), with the TTLs entry of the collector's version.
        private static AlertUpdateRequest ServiceAliveAlert(string template = "[$product]$path") => new()
        {
            Conditions = null,
            Status = SensorStatus.Error,
            DestinationMode = AlertDestinationMode.FromParent,
            Template = template,
            Icon = "\U0001F550",
            ScheduledNotificationTime = new DateTime(1, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            ScheduledRepeatMode = HSMSensorDataObjects.SensorRequests.AlertRepeatMode.Hourly,
            ScheduledInstantSend = true,
        };

        private static AddOrUpdateSensorRequest Registration(string path, List<long?> ttls, List<AlertUpdateRequest> ttlAlerts = null, bool force = false) => new()
        {
            Path = path,
            SensorType = SensorType.BooleanSensor,
            Alerts = [],
            TtlAlerts = ttlAlerts ?? [ServiceAliveAlert()],
            TTLs = ttls,
            KeepHistory = TimeSpan.FromDays(180).Ticks,
            AggregateData = true,
            EnableGrafana = true,
            Statistics = HSMSensorDataObjects.SensorRequests.StatisticsOptions.None,
            DisplayUnit = 0,
            IsForceUpdate = force,
        };

        private async Task RegisterAsync(AddOrUpdateSensorRequest request)
        {
            var result = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(TestProductsManager.ProductId, request.Path)
            {
                Update = request.Convert(Guid.Empty, "collectorKey"),
                Type = HSMCommon.Model.SensorType.Boolean,
                TypeIsExplicit = true,
            });

            Assert.True(result.IsOk, result.Error);
        }

        private Task RegisterOldCollectorAsync(string path) => RegisterAsync(Registration(path, [null]));

        private Task RegisterNewCollectorAsync(string path, bool force = false) => RegisterAsync(Registration(path, [FiveMinutes], force: force));

        private BaseSensorModel Sensor(string path)
        {
            Assert.True(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, path, out var sensor));
            return sensor;
        }

        // An operator's edit of one TTL alert, sent like the alert editor sends it: the node's full
        // TTL list (an unlisted alert would be dropped), the edited entry changed.
        private async Task EditTtlAlertAsync(BaseSensorModel sensor, TTLPolicy policy, Func<PolicyUpdate, PolicyUpdate> edit)
        {
            static PolicyUpdate Copy(TTLPolicy p) => new(p, Operator) { TTL = p.IsTTLFromParent ? null : p.TTLTicks };

            var result = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = Operator,
                TTLPolicies = sensor.Policies.TTLPolicies.Select(p => p.Id == policy.Id ? edit(Copy(p)) : Copy(p)).ToList(),
            });

            Assert.True(result.IsOk, result.Error);
        }

        private static InitiatorType OwnerOf(BaseSensorModel sensor, TTLPolicy policy) =>
            sensor.ChangeTable.TtlPolicies.TryGetValue(policy.Id.ToString(), out var info) ? info.Initiator.Type : InitiatorType.System;


        [Fact]
        public async Task ExistingServiceAlive_WithoutInterval_TakesTheFiveMinutesOnTheFirstReregistration()
        {
            var path = "svcalive/upgrade/.module/Service alive";

            await RegisterOldCollectorAsync(path);

            var sensor = Sensor(path);
            var before = Assert.Single(sensor.Policies.TTLPolicies);
            Assert.True(before.IsTTLFromParent);
            Assert.True(before.TTLInterval.IsNone); // the product has default settings: never fires

            await RegisterNewCollectorAsync(path);

            var after = Assert.Single(sensor.Policies.TTLPolicies);
            Assert.False(after.IsTTLFromParent);
            Assert.Equal(FiveMinutes, after.TTLTicks);
            Assert.NotEqual(InitiatorType.User, OwnerOf(sensor, after));

            // Repeated restarts keep exactly one alert.
            await RegisterNewCollectorAsync(path);
            Assert.Equal(FiveMinutes, Assert.Single(sensor.Policies.TTLPolicies).TTLTicks);
        }

        [Fact]
        public async Task OperatorEditedInterval_SurvivesACollectorReregistration()
        {
            var path = "svcalive/edited/.module/Service alive";

            await RegisterNewCollectorAsync(path);

            var sensor = Sensor(path);
            await EditTtlAlertAsync(sensor, Assert.Single(sensor.Policies.TTLPolicies), u => u with { TTL = ThirtyMinutes });

            var edited = Assert.Single(sensor.Policies.TTLPolicies);
            Assert.Equal(ThirtyMinutes, edited.TTLTicks);
            Assert.Equal(InitiatorType.User, OwnerOf(sensor, edited));

            await RegisterNewCollectorAsync(path);

            // The operator's alert stays, unchanged, and the collector's is not added next to it.
            var after = Assert.Single(sensor.Policies.TTLPolicies);
            Assert.Equal(edited.Id, after.Id);
            Assert.Equal(ThirtyMinutes, after.TTLTicks);
            Assert.Equal(InitiatorType.User, OwnerOf(sensor, after));
        }

        [Fact]
        public async Task OperatorDisabledAlert_StaysDisabledAfterACollectorReregistration()
        {
            var path = "svcalive/disabled/.module/Service alive";

            await RegisterNewCollectorAsync(path);

            var sensor = Sensor(path);
            await EditTtlAlertAsync(sensor, Assert.Single(sensor.Policies.TTLPolicies), u => u with { IsDisabled = true });
            Assert.True(Assert.Single(sensor.Policies.TTLPolicies).IsDisabled);

            await RegisterNewCollectorAsync(path);

            var after = Assert.Single(sensor.Policies.TTLPolicies);
            Assert.True(after.IsDisabled);
            Assert.Equal(FiveMinutes, after.TTLTicks);
        }

        [Fact]
        public async Task OperatorEditedAlert_SurvivesAnOldCollectorsReregistrationToo()
        {
            var path = "svcalive/edited-old/.module/Service alive";

            await RegisterOldCollectorAsync(path);

            var sensor = Sensor(path);
            await EditTtlAlertAsync(sensor, Assert.Single(sensor.Policies.TTLPolicies), u => u with { TTL = ThirtyMinutes });

            await RegisterOldCollectorAsync(path);

            Assert.Equal(ThirtyMinutes, Assert.Single(sensor.Policies.TTLPolicies).TTLTicks);
        }

        [Fact]
        public async Task ForcedReregistration_ReplacesTheOperatorsAlert()
        {
            var path = "svcalive/forced/.module/Service alive";

            await RegisterNewCollectorAsync(path);

            var sensor = Sensor(path);
            await EditTtlAlertAsync(sensor, Assert.Single(sensor.Policies.TTLPolicies), u => u with { TTL = ThirtyMinutes });

            await RegisterNewCollectorAsync(path, force: true);

            Assert.Equal(FiveMinutes, Assert.Single(sensor.Policies.TTLPolicies).TTLTicks);
        }

        // A collector registering several TTL alerts for one sensor: the operator edited one of
        // them. The re-registration leaves the whole list alone — the edited alert and the other
        // one — rather than dropping what the operator did not touch and adding nothing.
        [Fact]
        public async Task OperatorEditedOneOfSeveralAlerts_TheReregistrationLeavesTheListAlone()
        {
            var path = "svcalive/several/.module/Service alive";

            List<AlertUpdateRequest> Alerts() => [ServiceAliveAlert("first"), ServiceAliveAlert("second")];

            await RegisterAsync(Registration(path, [FiveMinutes, ThirtyMinutes], Alerts()));

            var sensor = Sensor(path);
            Assert.Equal(2, sensor.Policies.TTLPolicies.Count);

            var first = sensor.Policies.TTLPolicies.Single(p => p.Template == "first");
            var second = sensor.Policies.TTLPolicies.Single(p => p.Template == "second");
            await EditTtlAlertAsync(sensor, first, u => u with { TTL = TimeSpan.FromMinutes(7).Ticks });
            Assert.Equal(2, sensor.Policies.TTLPolicies.Count);

            await RegisterAsync(Registration(path, [FiveMinutes, ThirtyMinutes], Alerts()));

            Assert.Equal(2, sensor.Policies.TTLPolicies.Count);
            Assert.Equal(TimeSpan.FromMinutes(7).Ticks, sensor.Policies.TTLPolicies.Single(p => p.Id == first.Id).TTLTicks);
            Assert.Equal(ThirtyMinutes, sensor.Policies.TTLPolicies.Single(p => p.Id == second.Id).TTLTicks);
        }

        // The behaviour change #1666 accepts: once the collector's alert carries its own
        // interval, an inactivity period set on the sensor (or a parent) no longer drives it.
        // The setting itself is kept.
        [Fact]
        public async Task SensorInactivityPeriod_NoLongerDrivesTheAlertOnceItCarriesItsOwnInterval()
        {
            var path = "svcalive/setting/.module/Service alive";

            await RegisterOldCollectorAsync(path);

            var sensor = Sensor(path);
            var set = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = Operator,
                TTL = new TimeIntervalModel(ThirtyMinutes),
            });
            Assert.True(set.IsOk, set.Error);

            // Without an interval of its own the alert follows the sensor's setting.
            Assert.Equal(ThirtyMinutes, Assert.Single(sensor.Policies.TTLPolicies).TTLInterval.Ticks);

            await RegisterNewCollectorAsync(path);

            Assert.Equal(FiveMinutes, Assert.Single(sensor.Policies.TTLPolicies).TTLTicks);
            Assert.Equal(ThirtyMinutes, sensor.Settings.TTL.Value.Ticks);
        }
    }
}
