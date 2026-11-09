using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.StatisticInfo;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Model.ManagementApi.SensorTree;
using Moq;
using Xunit;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Core.Tests.Model.ManagementApi
{
    // The sensor-deletion write engine (#1637) at its seam: a REAL service over
    // a mocked cache whose RemoveSensorAsync mirrors the core entry (removes
    // the sensor from the cache lists, like the queue-thread dispatch does).
    // The suite pins the delegation (the SAME RemoveSensorAsync the web UI's
    // remove-node drives, with the token owner as initiator), the honest
    // post-write verification (a removal that did not apply answers Conflict,
    // never a ghost success), the dry-run preview (nothing delegated), and the
    // authorization mapping (read-only/Viewer -> 403, unknown/invisible -> the
    // same 404).
    public class SensorDeletionAdministrationServiceTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();

        private readonly Core.Model.ProductModel _product =
            new(EntitiesFactory.BuildProductEntity(name: "alpha") with { Id = Guid.NewGuid().ToString() });

        private readonly List<BaseSensorModel> _sensors = [];

        private readonly List<(Guid SensorId, InitiatorInfo Initiator)> _removeCalls = [];

        private readonly Dictionary<Guid, SensorHistoryInfo> _historyInfo = [];

        // The verification-failure probe: when set, the mocked core removal
        // records the call but removes nothing (the queue's swallow-only error
        // channel — RemoveSensorAsync returns void and logs internally).
        private bool _removalDoesNotApply;


        public SensorDeletionAdministrationServiceTests()
        {
            _cache.Setup(c => c.GetSensor(It.IsAny<Guid>()))
                .Returns((Guid id) => _sensors.FirstOrDefault(s => s.Id == id));

            // The queue-thread dispatch the real cache performs for its own
            // RemoveSensorAsync: the sensor leaves the cache before the task
            // resolves (mirroring RemoveSensor's order — cache first, then the
            // journal/DB writes), so the service's post-write read observes
            // the removal.
            _cache.Setup(c => c.RemoveSensorAsync(It.IsAny<Guid>(), It.IsAny<InitiatorInfo>(), It.IsAny<Guid?>(), It.IsAny<System.Threading.CancellationToken>()))
                .Returns((Guid id, InitiatorInfo initiator, Guid? _, System.Threading.CancellationToken _) =>
                {
                    _removeCalls.Add((id, initiator));

                    if (_removalDoesNotApply)
                        return Task.CompletedTask;

                    _sensors.RemoveAll(s => s.Id == id);
                    _product.RemoveSensor(id);

                    return Task.CompletedTask;
                });

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);

            _cache.Setup(c => c.GetSensorHistoryInfo(It.IsAny<Guid>()))
                .Returns((Guid id) => _historyInfo.GetValueOrDefault(id));

            _users.Setup(u => u[It.IsAny<Guid>()]).Returns(new User("api-owner"));
        }


        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private SensorDeletionAdministrationService CreateService() =>
            new(_cache.Object, _authorization.Object, _users.Object);

        private BaseSensorModel AddSensor(string name)
        {
            var sensor = Infrastructure.SensorModelFactory.Build(
                EntitiesFactory.BuildSensorEntity(name: name, type: (byte)SensorType.Integer) with
                {
                    Id = Guid.NewGuid().ToString(),
                });

            _product.AddSensor(sensor);
            _sensors.Add(sensor);

            return sensor;
        }


        // === Deletion: the delegation ===

        [Fact]
        public async Task DeleteSensor_DelegatesToTheCoresRemoval_WithTheTokenOwnerAsInitiator()
        {
            var sensor = AddSensor("cpu");

            var result = await CreateService().DeleteSensorAsync(sensor.Id, BuildPrincipal());

            Assert.True(result.Success, result.Failure?.Message);
            Assert.False(result.IsDryRun);

            var call = Assert.Single(_removeCalls);

            Assert.Equal(sensor.Id, call.SensorId);
            Assert.Equal(InitiatorType.User, call.Initiator.Type);
            Assert.Equal("api-owner", call.Initiator.Info);
        }


        // === Dry run: the preview ===

        [Fact]
        public async Task DeleteSensor_DryRun_PreviewsEverythingTheWriteWouldDestroy_WithoutTouchingAnything()
        {
            var sensor = AddSensor("cpu");
            var dataPolicy = SeedDataPolicy(sensor);
            var ttlPolicy = SeedTtlPolicy(sensor);
            var valueTime = new DateTime(2026, 11, 9, 10, 11, 12, DateTimeKind.Utc);

            Assert.True(sensor.TryAddValue(new IntegerValue { Time = valueTime, Value = 7 }), "value seed");

            _historyInfo[sensor.Id] = new SensorHistoryInfo
            {
                DataCount = 42,
                KeysSizeBytes = 100,
                ValuesSizeBytes = 500,
            };

            var result = await CreateService().DeleteSensorAsync(sensor.Id, BuildPrincipal(),
                new ChangeSetWriteOptions { DryRun = true });

            Assert.True(result.IsDryRun);

            var preview = Assert.IsType<SensorDeletePreviewDto>(result.Preview);

            Assert.True(preview.DryRun);
            Assert.Equal("sensor", preview.Kind);
            Assert.Equal("delete", preview.Operation);
            Assert.Equal(sensor.Id.ToString(), preview.EntityId);
            Assert.Equal(sensor.FullPath, preview.Path);
            Assert.Equal("alpha", preview.Product.Name);
            Assert.Equal(_product.Id, preview.Product.Id);
            Assert.Equal(nameof(SensorState.Available), preview.State);
            Assert.Null(preview.EndOfMuting); // not muted
            Assert.Equal(valueTime, preview.LastValueTime);
            Assert.Equal(42L, preview.HistoryValues);
            Assert.Equal(600L, preview.HistorySizeBytes); // keys + values

            // The affected report: the sensor itself plus every attached policy.
            Assert.Equal(3, preview.Affected.Count);
            Assert.Contains(preview.Affected, a => a.Id == sensor.Id.ToString() && a.Change == "remove sensor");
            Assert.Contains(preview.Affected, a => a.Id == dataPolicy.ToString() && a.Change == "remove data policy");
            Assert.Contains(preview.Affected, a => a.Id == ttlPolicy.ToString() && a.Change == "remove ttl policy");

            // Nothing was applied: no core removal, the sensor still reads back.
            Assert.Empty(_removeCalls);
            Assert.Contains(sensor, _sensors);
        }

        [Fact]
        public async Task DeleteSensor_DryRun_AMutedSensorPreviewsItsMuteState()
        {
            var sensor = AddSensor("cpu");
            var until = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            Assert.True(sensor.TryUpdate(new SensorUpdate
            {
                Id = sensor.Id,
                State = SensorState.Muted,
                EndOfMutingPeriod = until,
                Initiator = InitiatorInfo.AsSystemForce("seed"),
            }, out _), "mute seed");

            var result = await CreateService().DeleteSensorAsync(sensor.Id, BuildPrincipal(),
                new ChangeSetWriteOptions { DryRun = true });

            var preview = Assert.IsType<SensorDeletePreviewDto>(result.Preview);

            Assert.Equal(nameof(SensorState.Muted), preview.State);
            Assert.Equal(until, preview.EndOfMuting);
        }


        // === Authorization mapping (the evaluator's 404/403 split) ===

        [Fact]
        public async Task DeleteSensor_EvaluatorForbidden_Is403_AndTheWriteNeverRuns()
        {
            var sensor = AddSensor("cpu");

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var result = await CreateService().DeleteSensorAsync(sensor.Id, BuildPrincipal());

            Assert.Equal(PolicyWriteOutcome.Forbidden, result.Failure.Outcome);
            Assert.Equal(PolicyWriteResult.WriteDeniedMessage, result.Failure.Message);
            Assert.Empty(_removeCalls);
        }

        [Fact]
        public async Task DeleteSensor_UnknownSensor_IsTheSameOutcome_AsInvisible()
        {
            var sensor = AddSensor("cpu");

            // Unknown id: the evaluator is still consulted FIRST (it owns the
            // split) and allows — the plain area 404 comes from the lookup.
            var unknown = await CreateService().DeleteSensorAsync(Guid.NewGuid(), BuildPrincipal());

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            var invisible = await CreateService().DeleteSensorAsync(sensor.Id, BuildPrincipal());

            Assert.Equal(PolicyWriteOutcome.NotFound, unknown.Failure.Outcome);
            Assert.Equal(PolicyWriteOutcome.NotFound, invisible.Failure.Outcome);
            Assert.Empty(_removeCalls);
        }

        [Fact]
        public async Task DeleteSensor_SecondCallAfterRemoval_IsNotFound()
        {
            var sensor = AddSensor("cpu");
            var service = CreateService();

            Assert.True((await service.DeleteSensorAsync(sensor.Id, BuildPrincipal())).Success);

            var repeat = await service.DeleteSensorAsync(sensor.Id, BuildPrincipal());

            Assert.Equal(PolicyWriteOutcome.NotFound, repeat.Failure.Outcome);
            Assert.Single(_removeCalls); // the repeat never reached the core
        }

        [Fact]
        public async Task DeleteSensor_BlockedSensor_IsDeletedToo()
        {
            // Unlike the mute engine (a Blocked sensor is not MUTABLE), the
            // web UI's remove-node deletes any state — decommissioning a
            // rogue Blocked sensor is a legitimate delete.
            var sensor = AddSensor("cpu");

            Assert.True(sensor.TryUpdate(new SensorUpdate
            {
                Id = sensor.Id,
                State = SensorState.Blocked,
                Initiator = InitiatorInfo.AsSystemForce("seed"),
            }, out _), "block seed");

            var result = await CreateService().DeleteSensorAsync(sensor.Id, BuildPrincipal());

            Assert.True(result.Success, result.Failure?.Message);
            Assert.Single(_removeCalls);
        }

        [Fact]
        public async Task DeleteSensor_RemovalThatDoesNotApply_IsConflict_NotGhostSuccess()
        {
            // RemoveSensorAsync swallows its failures and returns void — the
            // post-write read is the honest-reporting channel (the policy
            // deletes' SendAndVerifyRemoval rule, inverted).
            var sensor = AddSensor("cpu");

            _removalDoesNotApply = true;

            var result = await CreateService().DeleteSensorAsync(sensor.Id, BuildPrincipal());

            Assert.Equal(PolicyWriteOutcome.Conflict, result.Failure.Outcome);
            Assert.Equal("The sensor could not be removed.", result.Failure.Message);
        }


        // === Test vocabulary ===

        // The PolicyAdministrationServiceTests seeding shape: Destination is
        // REQUIRED (a null destination update NREs inside Policy.TryUpdate and
        // the policy silently never lands), an integer Value condition keeps
        // the policy valid. Returns the STORED policy id.
        private static Guid SeedDataPolicy(BaseSensorModel sensor)
        {
            var id = Guid.NewGuid();

            Assert.True(sensor.TryUpdate(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = InitiatorInfo.AsSystemForce("seed"),
                Policies =
                [
                    new PolicyUpdate
                    {
                        Id = id,
                        Template = "seed",
                        Icon = "seed-icon",
                        Destination = new PolicyDestinationUpdate(),
                        Conditions =
                        [
                            new PolicyConditionUpdate(PolicyOperation.GreaterThan, PolicyProperty.Value,
                                new TargetValue(TargetType.Const, "10")),
                        ],
                        Initiator = InitiatorInfo.AsSystemForce("seed"),
                    },
                ],
            }, out _), "data policy seed");

            return id;
        }

        // The TTL seeding shape of the policy engine's tests: an explicit
        // interval, default destination, no conditions. Returns the STORED
        // policy id (the update does not carry one; the core generates it).
        private static Guid SeedTtlPolicy(BaseSensorModel sensor)
        {
            Assert.True(sensor.TryUpdate(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = InitiatorInfo.AsSystemForce("seed"),
                TTLPolicies =
                [
                    new PolicyUpdate
                    {
                        TTL = TimeSpan.FromMinutes(30).Ticks,
                        Initiator = InitiatorInfo.AsSystemForce("seed"),
                        Destination = new PolicyDestinationUpdate(),
                        Conditions = [],
                    },
                ],
            }, out _), "ttl policy seed");

            return sensor.Policies.TTLPolicies.Single().Id;
        }
    }
}
