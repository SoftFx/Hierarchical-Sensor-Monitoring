using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Controllers;
using HSMServer.Folders;
using HSMServer.Model.Folders;
using HSMServer.Model.ManagementApi.AlertTemplates;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

using TestSensorModelFactory = HSMServer.Core.Tests.Infrastructure.SensorModelFactory;

namespace HSMServer.Core.Tests.Controllers
{
    // REST CRUD over alert templates (#1351): the /api/v1 area conventions (attributes
    // the guard middleware admits), per-folder authorization mapped onto the 403/404
    // split, pagination, the ported web-UI validation rules, write-side normalizations,
    // and the full create -> get -> update -> delete round-trip.
    public class AlertTemplatesApiControllerTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IFolderManager> _folders = new();
        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IAlertScheduleProvider> _schedules = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();

        // Dictionary-backed stand-in reproducing the cache's upsert-by-id semantics, so
        // round-trip tests run against real storage behavior rather than loose stubs.
        private readonly ConcurrentDictionary<Guid, AlertTemplateModel> _store = new();


        public AlertTemplatesApiControllerTests()
        {
            _cache.Setup(c => c.GetAlertTemplateModels()).Returns(() => [.. _store.Values.ToList()]);
            _cache.Setup(c => c.GetAlertTemplate(It.IsAny<Guid>()))
                .Returns((Guid id) => _store.TryGetValue(id, out var template) ? template : null);
            _cache.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel>());
            _cache.Setup(c => c.AddAlertTemplateAsync(It.IsAny<AlertTemplateModel>(), It.IsAny<CancellationToken>()))
                .Returns((AlertTemplateModel model, CancellationToken _) =>
                {
                    _store[model.Id] = model;
                    return Task.FromResult((true, (string)null));
                });
            _cache.Setup(c => c.RemoveAlertTemplateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((Guid id, CancellationToken token) =>
                {
                    _store.TryRemove(id, out _);
                    return Task.FromResult((true, (string)null));
                });

            _authorization.Setup(a => a.Authorize(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<ApiTokenResource>()))
                .Returns(true);

            _chats.Setup(c => c.GetValues()).Returns(new List<Chat>());
            _schedules.Setup(s => s.GetSchedule(It.IsAny<Guid>())).Returns((AlertSchedule)null);
        }


        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private AlertTemplatesApiController CreateController() =>
            new(_cache.Object, _folders.Object, _chats.Object, _schedules.Object, _authorization.Object,
                NullLogger<AlertTemplatesApiController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
                },
            };

        private static AlertTemplateDto BuildDto(string name = null, byte sensorType = (byte)SensorType.Integer) => new()
        {
            Name = name ?? $"template-{Guid.NewGuid():N}",
            SensorType = sensorType,
            FolderId = Guid.NewGuid(),
            Paths = ["*/cpu"],
            Policies =
            [
                new AlertPolicyDto
                {
                    Conditions =
                    [
                        new PolicyConditionDto
                        {
                            Target = new PolicyTargetDto { Type = (byte)TargetType.Const, Value = "42" },
                            Combination = (byte)PolicyCombination.And,
                            Operation = (byte)PolicyOperation.GreaterThan,
                            Property = (byte)PolicyProperty.Value,
                        },
                    ],
                    SensorStatus = (byte)SensorStatus.Ok,
                },
            ],
            TtlPolicies = [new AlertPolicyDto { SensorStatus = (byte)SensorStatus.Ok }],
            Ttls = [new TimeIntervalDto { Interval = (long)TimeInterval.Ticks, Ticks = TimeSpan.FromMinutes(1).Ticks }],
        };

        private static BaseSensorModel BuildSensor(SensorType type) =>
            TestSensorModelFactory.Build(new SensorEntity
            {
                Id = Guid.NewGuid().ToString(),
                Type = (byte)type,
            });

        private static int StatusCodeOf(IActionResult result) =>
            result switch
            {
                ObjectResult objectResult => objectResult.StatusCode ?? throw new InvalidOperationException("no status"),
                StatusCodeResult codeResult => codeResult.StatusCode,
                _ => throw new InvalidOperationException($"unexpected result type {result.GetType().Name}"),
            };

        private void SetupFolderWithChats(Guid folderId, params Guid[] chatIds)
        {
            var folder = new FolderModel(EntitiesFactory.BuildFolderEntity() with
            {
                Id = folderId.ToString(),
                Chats = [.. chatIds.Select(id => id.ToByteArray())],
            });

            _folders.Setup(f => f.TryGetValue(folderId, out folder)).Returns(true);
        }

        private void SetupChat(Guid chatId, string name, params Guid[] folderIds)
        {
            var chat = new Chat(new ChatEntity
            {
                Id = chatId.ToByteArray(),
                Author = Guid.NewGuid().ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = name,
                SendMessages = true,
                MessagesAggregationTimeSec = 60,
            });

            foreach (var folderId in folderIds)
                chat.Folders.Add(folderId);

            _chats.Setup(c => c.GetValues()).Returns(new List<Chat> { chat });
        }


        [Fact]
        public void Controller_ClassCarriesManagementAreaMetadata()
        {
            // Without exactly this combination ManagementApiGuardMiddleware 404s every
            // endpoint before authentication; BaseController would drag the controller
            // into the cookie world.
            var type = typeof(AlertTemplatesApiController);

            Assert.NotNull(type.GetCustomAttribute<ManagementApiAttribute>());
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());

            var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorize);
            Assert.Equal(HsmApiTokenDefaults.ManagementPolicy, authorize.Policy);

            Assert.Equal("api/v1/alertTemplates", type.GetCustomAttribute<RouteAttribute>()?.Template);

            Assert.True(typeof(ControllerBase).IsAssignableFrom(type));
            Assert.False(typeof(BaseController).IsAssignableFrom(type));
        }


        [Fact]
        public void GetTemplates_FiltersByIsVisible_AndOrdersByNameThenId()
        {
            var folderA = Guid.NewGuid();
            var folderB = Guid.NewGuid();

            // "zeta" and "alpha" are visible; the out-of-reach folder-B template "beta"
            // must not appear; the visible items come back ordered by name.
            _store[Guid.NewGuid()] = new AlertTemplateModel { Name = "zeta", FolderId = folderA };
            _store[Guid.NewGuid()] = new AlertTemplateModel { Name = "beta", FolderId = folderB };
            _store[Guid.NewGuid()] = new AlertTemplateModel { Name = "alpha", FolderId = folderA };

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, string _, ApiTokenResource resource) => resource.Id != folderB);

            var page = Assert.IsType<OkObjectResult>(CreateController().GetTemplates()).Value as AlertTemplatePageDto;

            Assert.NotNull(page);
            Assert.Equal(2, page.TotalCount);
            Assert.Equal(["alpha", "zeta"], page.Items.Select(t => t.Name).ToArray());
        }

        [Fact]
        public void GetTemplates_ListsUnderTheReadOperation_NotJustReach()
        {
            // A list returns full bodies: an item may appear only under the SAME
            // operation its item endpoint would demand (alerts:read). A token whose
            // grants reach the folder without alerts:read (the old any-operation
            // predicate) must see an empty page.
            var folder = Guid.NewGuid();
            _store[Guid.NewGuid()] = new AlertTemplateModel { Name = "secret", FolderId = folder };

            string askedOperation = null;
            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, string operation, ApiTokenResource _) =>
                {
                    askedOperation = operation;
                    return false; // the evaluator denies alerts:read
                })
                .Verifiable();

            var page = Assert.IsType<OkObjectResult>(CreateController().GetTemplates()).Value as AlertTemplatePageDto;

            Assert.Equal(ApiTokenOperations.AlertsRead, askedOperation);
            Assert.Empty(page.Items);
            Assert.Equal(0, page.TotalCount);
            _authorization.Verify();
        }

        [Fact]
        public void GetTemplates_MemoizesDecision_PerDistinctFolder()
        {
            var folderA = Guid.NewGuid();
            var folderB = Guid.NewGuid();

            foreach (var index in Enumerable.Range(0, 6))
                _store[Guid.NewGuid()] = new AlertTemplateModel { Name = $"t{index}", FolderId = index % 2 == 0 ? folderA : folderB };

            CreateController().GetTemplates();

            // Six templates, two distinct folders: the evaluator runs twice, not six
            // times (it re-resolves user + token + grants on every call).
            _authorization.Verify(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<ApiTokenResource>()), Times.Exactly(2));
        }

        [Fact]
        public void GetTemplates_Paginates()
        {
            foreach (var index in Enumerable.Range(0, 5))
                _store[Guid.NewGuid()] = new AlertTemplateModel { Name = $"t{index}" };

            var page = Assert.IsType<OkObjectResult>(CreateController().GetTemplates(page: 2, pageSize: 2))
                .Value as AlertTemplatePageDto;

            Assert.NotNull(page);
            Assert.Equal(5, page.TotalCount);
            Assert.Equal(3, page.TotalPages);
            Assert.Equal(2, page.Page);
            Assert.Equal(2, page.PageSize);
            Assert.Equal(["t2", "t3"], page.Items.Select(t => t.Name).ToArray());
        }

        [Fact]
        public void GetTemplates_ClampsOutOfRangePaging()
        {
            foreach (var index in Enumerable.Range(0, 3))
                _store[Guid.NewGuid()] = new AlertTemplateModel { Name = $"t{index}" };

            var clamped = Assert.IsType<OkObjectResult>(CreateController().GetTemplates(page: 0, pageSize: 9_999))
                .Value as AlertTemplatePageDto;

            Assert.NotNull(clamped);
            Assert.Equal(1, clamped.Page);
            Assert.Equal(AlertTemplatesApiController.MaxPageSize, clamped.PageSize);
            Assert.Equal(3, clamped.TotalCount);
        }

        [Fact]
        public void GetTemplates_HugePageNumber_ClampsToLastPage()
        {
            // (page - 1) * pageSize must never overflow int: a wrapped NEGATIVE Skip
            // count would silently return the FIRST page labeled as page N.
            foreach (var index in Enumerable.Range(0, 3))
                _store[Guid.NewGuid()] = new AlertTemplateModel { Name = $"t{index}" };

            var page = Assert.IsType<OkObjectResult>(CreateController().GetTemplates(page: 429_496_747, pageSize: 2))
                .Value as AlertTemplatePageDto;

            Assert.NotNull(page);
            Assert.Equal(2, page.Page); // clamped to totalPages
            Assert.Equal(["t2"], page.Items.Select(t => t.Name).ToArray());
        }


        [Theory]
        [InlineData(ApiTokenAuthorization.Allowed, 200)]
        [InlineData(ApiTokenAuthorization.Forbidden, 403)]
        [InlineData(ApiTokenAuthorization.NotFound, 404)]
        public void GetTemplate_EvaluatorDecision_MapsToStatus(ApiTokenAuthorization decision, int expected)
        {
            var template = new AlertTemplateModel { Name = "t" };
            _store[template.Id] = template;

            _authorization.Setup(a => a.Authorize(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<ApiTokenResource>()))
                .Returns(decision);

            Assert.Equal(expected, StatusCodeOf(CreateController().GetTemplate(template.Id)));
        }

        [Fact]
        public void GetTemplate_MapsEntityShapeToDto()
        {
            // A well-formed model via the same reconstruction path the writes use.
            var source = BuildDto(name: "shaped") with { Paths = ["*/cpu", "*/mem"] };
            var model = new AlertTemplateModel(AlertTemplateDtoMapper.ToEntity(source, Guid.NewGuid(), new Dictionary<string, Chat>()));

            _store[model.Id] = model;

            var dto = Assert.IsType<OkObjectResult>(CreateController().GetTemplate(model.Id)).Value as AlertTemplateDto;

            Assert.NotNull(dto);
            Assert.Equal(model.Id, dto.Id);
            Assert.Equal("shaped", dto.Name);
            Assert.Equal(model.FolderId, dto.FolderId);
            Assert.Equal((byte)SensorType.Integer, dto.SensorType);
            Assert.Equal(["*/cpu", "*/mem"], dto.Paths);
            Assert.Single(dto.Policies);
            Assert.Single(dto.TtlPolicies);
            Assert.Single(dto.Ttls);
            Assert.All(dto.Policies, p => Assert.NotEqual(Guid.Empty, p.Id));
        }

        [Fact]
        public void GetTemplate_Absent_Is404()
        {
            Assert.IsType<NotFoundResult>(CreateController().GetTemplate(Guid.NewGuid()));
        }


        [Fact]
        public async Task Create_IgnoresClientId_AndReturns201WithLocation()
        {
            var clientId = Guid.NewGuid();
            var dto = BuildDto() with { Id = clientId };

            var result = Assert.IsType<CreatedAtActionResult>(await CreateController().CreateTemplate(dto));

            Assert.Equal(nameof(AlertTemplatesApiController.GetTemplate), result.ActionName);
            var serverId = Assert.IsType<Guid>(result.RouteValues["id"]);
            Assert.NotEqual(clientId, serverId);

            // The client-chosen id must not overwrite anything: only the server id is stored.
            Assert.DoesNotContain(clientId, _store.Keys);
            Assert.True(_store.ContainsKey(serverId));
        }

        [Fact]
        public async Task Create_FolderNotFoundByEvaluator_Is404_BeforeValidation()
        {
            // Authorization decides before any body validation runs — an invalid payload
            // in an unreachable folder is a 404, and the cache is never touched.
            _authorization.Setup(a => a.Authorize(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            var dto = BuildDto() with { Name = "" };

            Assert.IsType<NotFoundResult>(await CreateController().CreateTemplate(dto));
            _cache.Verify(c => c.AddAlertTemplateAsync(It.IsAny<AlertTemplateModel>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public async Task Create_MissingName_Is400(string name)
        {
            // BuildDto replaces a null name with a generated one, so the explicit null
            // is applied after the fact.
            var dto = BuildDto() with { Name = name };

            var result = await CreateController().CreateTemplate(dto);

            Assert.Equal(400, StatusCodeOf(result));
            Assert.Empty(_store);
        }

        [Fact]
        public async Task Create_NoPaths_Is400()
        {
            var dto = BuildDto() with { Paths = ["   "] };

            Assert.Equal(400, StatusCodeOf(await CreateController().CreateTemplate(dto)));
        }

        [Fact]
        public async Task Create_MissingFolderId_Is400_Not404()
        {
            // An all-zero folder id references no folder: a 400, not the evaluator's
            // 404 — and never a write into a "folder 0" template.
            var dto = BuildDto() with { FolderId = Guid.Empty };

            var result = await CreateController().CreateTemplate(dto);

            Assert.Equal(400, StatusCodeOf(result));
            Assert.Empty(_store);
            _authorization.Verify(a => a.Authorize(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<ApiTokenResource>()), Times.Never);
        }

        [Theory]
        [InlineData("policies")]
        [InlineData("ttlPolicies")]
        [InlineData("ttls")]
        public async Task Create_NullListElements_Are400(string listName)
        {
            // System.Text.Json materialises "policies": [null] as a real null element;
            // it must be rejected structurally, never dereferenced into a 500.
            var dto = BuildDto();

            switch (listName)
            {
                case "policies":
                    dto.Policies.Add(null);
                    break;
                case "ttlPolicies":
                    dto.TtlPolicies.Add(null);
                    break;
                default:
                    dto.Ttls.Add(null);
                    break;
            }

            var result = await CreateController().CreateTemplate(dto);

            Assert.Equal(400, StatusCodeOf(result));
            Assert.Empty(_store);
        }

        [Fact]
        public async Task Create_NullDestinationAndSchedule_MeanOmitted()
        {
            // "destination": null / "schedule": null are wire-omissions, not a domain
            // parse failure: the mapper substitutes the defaults instead of throwing a
            // misleading "condition is not supported" 400.
            var dto = BuildDto();
            dto.Policies[0] = dto.Policies[0] with { Destination = null, Schedule = null };

            Assert.Equal(201, StatusCodeOf(await CreateController().CreateTemplate(dto)));
            Assert.Empty(_store.Values.Single().Policies[0].Destination.Chats);
        }

        [Fact]
        public async Task Create_ExcessiveSizes_Are400()
        {
            // The web UI is bounded by its widgets; the API states its bounds.
            var tooManyPaths = BuildDto() with { Paths = [.. Enumerable.Range(0, AlertTemplatesApiController.MaxPaths + 1).Select(i => $"*/p{i}")] };
            Assert.Equal(400, StatusCodeOf(await CreateController().CreateTemplate(tooManyPaths)));

            var tooManyPolicies = BuildDto() with
            {
                Policies = [.. Enumerable.Range(0, AlertTemplatesApiController.MaxPolicies + 1)
                    .Select(_ => new AlertPolicyDto { SensorStatus = (byte)SensorStatus.Ok })],
                TtlPolicies = [],
                Ttls = [],
            };
            Assert.Equal(400, StatusCodeOf(await CreateController().CreateTemplate(tooManyPolicies)));

            var longName = BuildDto(name: new string('n', AlertTemplatesApiController.MaxNameLength + 1));
            Assert.Equal(400, StatusCodeOf(await CreateController().CreateTemplate(longName)));

            Assert.Empty(_store);
        }

        [Fact]
        public async Task Create_WriteIsNotClientCancellable_TheReconcileAlwaysCompletes()
        {
            // RequestAborted is deliberately NOT forwarded into the cache: Add persists
            // before reconciling and Remove strips per-sensor policies BEFORE deleting
            // the template — a client-triggered cancellation mid-loop would leave
            // half-applied state (a cancelled delete silently disarms alerts).
            _cache.Setup(c => c.AddAlertTemplateAsync(It.IsAny<AlertTemplateModel>(), It.IsAny<CancellationToken>()))
                .Callback((AlertTemplateModel model, CancellationToken token) => Assert.False(token.CanBeCanceled))
                .Returns((AlertTemplateModel model, CancellationToken _) =>
                {
                    _store[model.Id] = model;
                    return Task.FromResult((true, (string)null));
                });

            Assert.Equal(201, StatusCodeOf(await CreateController().CreateTemplate(BuildDto())));
        }

        [Fact]
        public async Task Create_UndefinedTtlInterval_Is400()
        {
            // TimeInterval is a sparse long enum; an undefined value persists fine but
            // throws NotImplementedException inside the timeout-scan loop (outside the
            // controller's try) — it must never be accepted. 60 was exactly the value
            // the original test fixture used.
            var dto = BuildDto() with { Ttls = [new TimeIntervalDto { Interval = 60, Ticks = 60 }] };

            var result = await CreateController().CreateTemplate(dto);

            Assert.Equal(400, StatusCodeOf(result));
            Assert.Empty(_store);
        }

        [Fact]
        public async Task Create_TtlTicksOutsideDateTimeRange_Is400()
        {
            // When ticks are authoritative, time.AddTicks(ticks) must stay inside the
            // DateTime range — an overflow throws in the same evaluation loop.
            var huge = BuildDto() with { Ttls = [new TimeIntervalDto { Interval = (long)TimeInterval.Ticks, Ticks = long.MaxValue }] };
            Assert.Equal(400, StatusCodeOf(await CreateController().CreateTemplate(huge)));

            var negative = BuildDto() with { Ttls = [new TimeIntervalDto { Interval = (long)TimeInterval.Ticks, Ticks = -1 }] };
            Assert.Equal(400, StatusCodeOf(await CreateController().CreateTemplate(negative)));

            Assert.Empty(_store);
        }

        [Fact]
        public async Task Create_DuplicatePolicyIds_Are400()
        {
            // The id becomes the per-sensor policy's TemplateAlertId — the matching key:
            // two policies sharing one id silently collapse into one at apply time.
            var sharedId = Guid.NewGuid();
            var dto = BuildDto() with
            {
                Policies =
                [
                    new AlertPolicyDto { Id = sharedId, SensorStatus = (byte)SensorStatus.Ok },
                    new AlertPolicyDto { Id = sharedId, SensorStatus = (byte)SensorStatus.Ok },
                ],
                TtlPolicies = [new AlertPolicyDto { Id = sharedId, SensorStatus = (byte)SensorStatus.Ok }],
                Ttls = [new TimeIntervalDto { Interval = (long)TimeInterval.Ticks, Ticks = TimeSpan.FromMinutes(1).Ticks }],
            };

            var result = await CreateController().CreateTemplate(dto);

            Assert.Equal(400, StatusCodeOf(result));
            Assert.Empty(_store);
        }

        [Fact]
        public async Task Create_UnknownScheduleId_Is400()
        {
            // The web UI's dropdown offers existing schedules only; a dangling id is
            // silently treated as always-in-working-time at evaluation.
            var scheduleId = Guid.NewGuid();
            var dto = BuildDto();
            dto.Policies[0] = dto.Policies[0] with { ScheduleId = scheduleId };

            Assert.Equal(400, StatusCodeOf(await CreateController().CreateTemplate(dto)));

            // The same id resolves once the schedule exists.
            _schedules.Setup(s => s.GetSchedule(scheduleId)).Returns(new AlertSchedule { Id = scheduleId });

            Assert.Equal(201, StatusCodeOf(await CreateController().CreateTemplate(dto)));
        }

        [Theory]
        [InlineData((byte)42)]
        [InlineData((byte)11)]
        public async Task Create_InvalidSensorType_Is400(byte sensorType)
        {
            var dto = BuildDto(sensorType: sensorType);

            Assert.Equal(400, StatusCodeOf(await CreateController().CreateTemplate(dto)));
        }

        [Fact]
        public async Task Create_AnyType_IsAccepted()
        {
            // AnyType policies are stored Boolean-shaped (the domain builds a
            // BooleanPolicy), so the AnyType payload carries no value policies here.
            var dto = BuildDto(sensorType: AlertTemplateModel.AnyType) with { Policies = [] };

            Assert.Equal(201, StatusCodeOf(await CreateController().CreateTemplate(dto)));
        }

        [Fact]
        public async Task Create_DuplicateName_Is400()
        {
            _store[Guid.NewGuid()] = new AlertTemplateModel { Name = "taken" };

            var dto = BuildDto(name: "taken");

            var result = await CreateController().CreateTemplate(dto);

            Assert.Equal(400, StatusCodeOf(result));
            Assert.Single(_store);
        }

        [Fact]
        public async Task Create_PathTypeMismatch_Is400()
        {
            _cache.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel> { BuildSensor(SensorType.Double) });

            var dto = BuildDto(sensorType: (byte)SensorType.Integer);

            var result = await CreateController().CreateTemplate(dto);

            Assert.Equal(400, StatusCodeOf(result));
            _cache.Verify(c => c.GetSensors(It.IsAny<string>(), null, It.IsAny<Guid?>()), Times.Once);
        }

        [Fact]
        public async Task Create_AnyTypeTemplate_SkipsMismatchCheck()
        {
            _cache.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel> { BuildSensor(SensorType.Double) });

            var dto = BuildDto(sensorType: AlertTemplateModel.AnyType) with { Policies = [] };

            Assert.Equal(201, StatusCodeOf(await CreateController().CreateTemplate(dto)));
            _cache.Verify(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()), Times.Never);
        }

        [Fact]
        public async Task Create_UnknownChat_Is400()
        {
            var chatId = Guid.NewGuid();
            var dto = BuildDto();
            dto.Policies[0] = dto.Policies[0] with
            {
                Destination = new PolicyDestinationDto { Chats = new Dictionary<string, string> { [chatId.ToString()] = "ops" } },
            };

            Assert.Equal(400, StatusCodeOf(await CreateController().CreateTemplate(dto)));
        }

        [Fact]
        public async Task Create_ChatBoundToAnotherFolder_Is400()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, "foreign", folderIds: Guid.NewGuid());

            var dto = BuildDto();
            dto.Policies[0] = dto.Policies[0] with
            {
                Destination = new PolicyDestinationDto { Chats = new Dictionary<string, string> { [chatId.ToString()] = "ops" } },
            };

            var result = await CreateController().CreateTemplate(dto);

            Assert.Equal(400, StatusCodeOf(result));
            Assert.Empty(_store);
        }

        [Fact]
        public async Task Create_GlobalChat_IsAccepted()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, "global"); // bound to no folder

            var dto = BuildDto();
            dto.Policies[0] = dto.Policies[0] with
            {
                Destination = new PolicyDestinationDto { Chats = new Dictionary<string, string> { [chatId.ToString()] = "stale-name" } },
            };

            Assert.Equal(201, StatusCodeOf(await CreateController().CreateTemplate(dto)));

            // The display name is canonicalized to the manager's current name on write.
            Assert.Equal("global", _store.Values.Single().Policies[0].Destination.Chats[chatId]);
        }

        [Fact]
        public async Task Create_ChatBoundToTemplateFolder_IsAccepted()
        {
            var folderId = Guid.NewGuid();
            var chatId = Guid.NewGuid();
            SetupChat(chatId, "folder-chat", folderIds: folderId);
            SetupFolderWithChats(folderId, chatId);

            var dto = BuildDto() with { FolderId = folderId };
            dto.Policies[0] = dto.Policies[0] with
            {
                Destination = new PolicyDestinationDto { Chats = new Dictionary<string, string> { [chatId.ToString()] = "folder-chat" } },
            };

            Assert.Equal(201, StatusCodeOf(await CreateController().CreateTemplate(dto)));
        }

        [Fact]
        public async Task Create_EmptyPolicyIds_AreRegenerated()
        {
            var dto = BuildDto();
            dto.Policies[0] = dto.Policies[0] with { Id = Guid.Empty };

            Assert.Equal(201, StatusCodeOf(await CreateController().CreateTemplate(dto)));
            Assert.NotEqual(Guid.Empty, _store.Values.Single().Policies[0].Id);
        }

        [Fact]
        public async Task Create_UnsupportedConditionProperty_Is400Not500()
        {
            // Min is a bar-sensor property: legal-looking for Integer, but the domain
            // throws while applying it — the API answers 400, never a 500.
            var dto = BuildDto(sensorType: (byte)SensorType.Integer);
            dto.Policies[0] = dto.Policies[0] with
            {
                Conditions = [new PolicyConditionDto
                {
                    Target = new PolicyTargetDto { Type = (byte)TargetType.Const, Value = "1" },
                    Operation = (byte)PolicyOperation.GreaterThan,
                    Property = (byte)PolicyProperty.Min,
                }],
            };

            Assert.Equal(400, StatusCodeOf(await CreateController().CreateTemplate(dto)));
        }

        [Fact]
        public async Task Create_MismatchedTtlListLengths_Is400()
        {
            var dto = BuildDto();
            dto.TtlPolicies.Add(new AlertPolicyDto { SensorStatus = (byte)SensorStatus.Ok });

            Assert.Equal(400, StatusCodeOf(await CreateController().CreateTemplate(dto)));
        }

        [Fact]
        public async Task Create_ScheduleTimeTicksOutOfRange_Is400()
        {
            var dto = BuildDto();
            dto.Policies[0] = dto.Policies[0] with
            {
                Schedule = new PolicyScheduleDto { TimeTicks = long.MinValue, InstantSend = true },
            };

            Assert.Equal(400, StatusCodeOf(await CreateController().CreateTemplate(dto)));
        }

        [Fact]
        public async Task Create_FolderWithoutProducts_Is409()
        {
            _cache.Setup(c => c.AddAlertTemplateAsync(It.IsAny<AlertTemplateModel>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult((false, "No products found in the selected folder.")));

            var result = await CreateController().CreateTemplate(BuildDto());

            Assert.Equal(409, StatusCodeOf(result));
            Assert.Contains("No products found", Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Detail);
        }


        [Fact]
        public async Task Update_RoundTripsThroughStoredModel()
        {
            var stored = new AlertTemplateModel { Name = "original", SensorType = (byte)SensorType.Integer, FolderId = Guid.NewGuid() };
            _store[stored.Id] = stored;

            var dto = BuildDto(name: "renamed");
            var putResult = Assert.IsType<OkObjectResult>(await CreateController().UpdateTemplate(stored.Id, dto));
            var echoed = Assert.IsType<AlertTemplateDto>(putResult.Value);

            var getResult = Assert.IsType<OkObjectResult>(CreateController().GetTemplate(stored.Id));
            var fetched = Assert.IsType<AlertTemplateDto>(getResult.Value);

            Assert.Equal(echoed.Id, fetched.Id);
            Assert.Equal(echoed.Name, fetched.Name);
            Assert.Equal(echoed.SensorType, fetched.SensorType);
            Assert.Equal(echoed.FolderId, fetched.FolderId);
            Assert.Equal(echoed.Paths, fetched.Paths);
            Assert.Equal(echoed.Policies.Count, fetched.Policies.Count);
            Assert.Equal(echoed.Policies[0].Id, fetched.Policies[0].Id);
            Assert.Equal(echoed.Policies[0].Conditions.Count, fetched.Policies[0].Conditions.Count);
            Assert.Equal(echoed.TtlPolicies.Count, fetched.TtlPolicies.Count);
            Assert.Equal(echoed.Ttls, fetched.Ttls);
        }

        [Fact]
        public async Task Update_Absent_Is404_WithoutEvaluatorCall()
        {
            Assert.IsType<NotFoundResult>(await CreateController().UpdateTemplate(Guid.NewGuid(), BuildDto()));

            _authorization.Verify(a => a.Authorize(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<ApiTokenResource>()), Times.Never);
        }

        [Fact]
        public async Task Update_FolderMove_RequiresWriteOnBothFolders()
        {
            var folderA = Guid.NewGuid();
            var folderB = Guid.NewGuid();

            var stored = new AlertTemplateModel { Name = "mover", FolderId = folderA };
            _store[stored.Id] = stored;

            var dto = BuildDto() with { FolderId = folderB };

            _authorization.Setup(a => a.Authorize(It.IsAny<ClaimsPrincipal>(), ApiTokenOperations.AlertsWrite,
                    It.Is<ApiTokenResource>(r => r.Id == folderA)))
                .Returns(ApiTokenAuthorization.Allowed);

            _authorization.Setup(a => a.Authorize(It.IsAny<ClaimsPrincipal>(), ApiTokenOperations.AlertsWrite,
                    It.Is<ApiTokenResource>(r => r.Id == folderB)))
                .Returns(ApiTokenAuthorization.Forbidden);

            Assert.Equal(403, StatusCodeOf(await CreateController().UpdateTemplate(stored.Id, dto)));
            Assert.Equal(folderA, _store[stored.Id].FolderId); // unchanged

            // Both sides allowed: the move happens.
            _authorization.Setup(a => a.Authorize(It.IsAny<ClaimsPrincipal>(), ApiTokenOperations.AlertsWrite,
                    It.Is<ApiTokenResource>(r => r.Id == folderB)))
                .Returns(ApiTokenAuthorization.Allowed);

            Assert.Equal(200, StatusCodeOf(await CreateController().UpdateTemplate(stored.Id, dto)));
            Assert.Equal(folderB, _store[stored.Id].FolderId);
        }

        [Fact]
        public async Task Update_MissingFolderId_Is400_ButOnlyAfterAuthorization()
        {
            var stored = new AlertTemplateModel { Name = "folderless", FolderId = Guid.NewGuid() };
            _store[stored.Id] = stored;

            // A body-shape 400 must not leak existence to a caller outside the
            // folder's reach: the evaluator decides FIRST (403 here), even though the
            // body omits folderId.
            _authorization.Setup(a => a.Authorize(It.IsAny<ClaimsPrincipal>(), ApiTokenOperations.AlertsWrite,
                    It.Is<ApiTokenResource>(r => r.Id == stored.FolderId)))
                .Returns(ApiTokenAuthorization.Forbidden);

            Assert.Equal(403, StatusCodeOf(await CreateController().UpdateTemplate(stored.Id, BuildDto() with { FolderId = Guid.Empty })));

            // An authorized caller gets the 400 instead.
            _authorization.Setup(a => a.Authorize(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);

            Assert.Equal(400, StatusCodeOf(await CreateController().UpdateTemplate(stored.Id, BuildDto() with { FolderId = Guid.Empty })));
            Assert.Equal(stored.FolderId, _store[stored.Id].FolderId); // untouched
        }

        [Fact]
        public async Task Update_BodyIdMismatchRoute_Is400()
        {
            var stored = new AlertTemplateModel { Name = "pinned" };
            _store[stored.Id] = stored;

            var dto = BuildDto() with { Id = Guid.NewGuid() };

            Assert.Equal(400, StatusCodeOf(await CreateController().UpdateTemplate(stored.Id, dto)));
        }

        [Fact]
        public async Task Update_KeepsOwnName_Allowed()
        {
            var stored = new AlertTemplateModel { Name = "stable-name" };
            _store[stored.Id] = stored;

            var dto = BuildDto(name: "stable-name");

            Assert.Equal(200, StatusCodeOf(await CreateController().UpdateTemplate(stored.Id, dto)));
        }


        [Fact]
        public async Task Delete_RemovesAndReturns204()
        {
            var stored = new AlertTemplateModel { Name = "doomed" };
            _store[stored.Id] = stored;

            var controller = CreateController();

            Assert.IsType<NoContentResult>(await controller.DeleteTemplate(stored.Id));
            Assert.IsType<NotFoundResult>(controller.GetTemplate(stored.Id));
        }

        [Fact]
        public async Task Delete_CacheFailure_Is409()
        {
            var stored = new AlertTemplateModel { Name = "stuck" };
            _store[stored.Id] = stored;

            _cache.Setup(c => c.RemoveAlertTemplateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult((false, "Failed to remove template from products: X")));

            var result = await CreateController().DeleteTemplate(stored.Id);

            Assert.Equal(409, StatusCodeOf(result));
        }

        [Fact]
        public async Task CrudLifecycle_CreateGetUpdateDelete()
        {
            var controller = CreateController();

            // Create.
            var dto = BuildDto(name: "lifecycle");
            var created = Assert.IsType<CreatedAtActionResult>(await controller.CreateTemplate(dto));
            var id = Assert.IsType<Guid>(created.RouteValues["id"]);

            // Get.
            var fetched = Assert.IsType<AlertTemplateDto>(Assert.IsType<OkObjectResult>(controller.GetTemplate(id)).Value);
            Assert.Equal("lifecycle", fetched.Name);

            // Update: rename + one more path.
            var updated = BuildDto(name: "lifecycle-2") with { Paths = ["*/cpu", "*/mem"] };
            Assert.Equal(200, StatusCodeOf(await controller.UpdateTemplate(id, updated)));

            var afterUpdate = Assert.IsType<AlertTemplateDto>(Assert.IsType<OkObjectResult>(controller.GetTemplate(id)).Value);
            Assert.Equal("lifecycle-2", afterUpdate.Name);
            Assert.Equal(["*/cpu", "*/mem"], afterUpdate.Paths);

            // Delete.
            Assert.IsType<NoContentResult>(await controller.DeleteTemplate(id));
            Assert.IsType<NotFoundResult>(controller.GetTemplate(id));
        }
    }
}
