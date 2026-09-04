using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Controllers;
using HSMServer.Folders;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.AlertTemplates;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // The uniform JSON error contract of the management API (#1353, epic #1347):
    // every error path of every /api/v1 resource controller answers with the same
    // wire shape — {error, message, details} — where `error` is a stable
    // machine-readable code an agent can switch on, `message` a human summary and
    // `details` field-keyed validation messages on 400s (null otherwise). The
    // contract is documented in aicontext/features/server/management-api/feature.md.
    public class ManagementApiErrorContractTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IFolderManager> _folders = new();
        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IAlertScheduleProvider> _schedulesProvider = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();

        private readonly List<AlertSchedule> _scheduleStore = [];


        public ManagementApiErrorContractTests()
        {
            _cache.Setup(c => c.GetAlertTemplateModels()).Returns(new List<AlertTemplateModel>());
            _cache.Setup(c => c.GetAlertTemplate(It.IsAny<Guid>())).Returns((AlertTemplateModel)null);
            _cache.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel>());
            _cache.Setup(c => c.AddAlertTemplateAsync(It.IsAny<AlertTemplateModel>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync((true, (string)null));
            _cache.Setup(c => c.RemoveAlertTemplateAsync(It.IsAny<Guid>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync((true, (string)null));

            _authorization.Setup(a => a.Authorize(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
            _authorization.Setup(a => a.HasOperationAtAnyVisibleBoundary(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>()))
                .Returns(true);

            _chats.Setup(c => c.GetValues()).Returns(new List<Chat>());
            _schedulesProvider.Setup(s => s.GetSchedule(It.IsAny<Guid>())).Returns((AlertSchedule)null);
            _schedulesProvider.Setup(s => s.GetAllSchedules()).Returns(() => _scheduleStore.ToList());
            _cache.Setup(c => c.GetSensorsByAlertSchedule(It.IsAny<Guid>())).Returns(new List<BaseSensorModel>());
        }


        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private AlertTemplatesApiController CreateTemplatesController() =>
            new(_cache.Object, _folders.Object, _chats.Object, _schedulesProvider.Object, _authorization.Object,
                NullLogger<AlertTemplatesApiController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
                },
            };

        private AlertSchedulesApiController CreateSchedulesController() =>
            new(_schedulesProvider.Object, _cache.Object, _authorization.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
                },
            };

        private static AlertTemplateDto BuildDto() => new()
        {
            Name = $"template-{Guid.NewGuid():N}",
            SensorType = (byte)SensorType.Integer,
            FolderId = Guid.NewGuid(),
            Paths = ["*/cpu"],
            Policies = [new AlertPolicyDto { SensorStatus = (byte)SensorStatus.Ok }],
            TtlPolicies = [new AlertPolicyDto { SensorStatus = (byte)SensorStatus.Ok }],
            Ttls = [new TimeIntervalDto { Interval = (long)TimeInterval.Ticks, Ticks = TimeSpan.FromMinutes(1).Ticks }],
        };

        private static ManagementApiErrorDto ErrorBodyOf(IActionResult result) =>
            Assert.IsType<ManagementApiErrorDto>(Assert.IsType<ObjectResult>(result).Value);

        private static (ManagementApiErrorDto Body, int? Status) BodyAndStatus(IActionResult result) =>
            (ErrorBodyOf(result), Assert.IsType<ObjectResult>(result).StatusCode);


        [Fact]
        public void Template_AbsentId_IsUniformNotFound()
        {
            var (body, status) = BodyAndStatus(CreateTemplatesController().GetTemplate(Guid.NewGuid()));

            Assert.Equal(404, status);
            Assert.Equal(ManagementApiErrors.NotFoundCode, body.Error);
            Assert.NotNull(body.Message);
            Assert.Null(body.Details);
        }

        [Fact]
        public void Template_InvisibleFolder_Is404_IndistinguishableFromAbsent()
        {
            // Anti-enumeration: the evaluator's NotFound and an unknown id must produce
            // the SAME body — same error code, same message.
            var template = BuildStoredTemplate(Guid.NewGuid());

            _authorization.Setup(a => a.Authorize(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            var absent = ErrorBodyOf(CreateTemplatesController().GetTemplate(Guid.NewGuid()));
            var (invisible, status) = BodyAndStatus(CreateTemplatesController().GetTemplate(template.Id));

            Assert.Equal(404, status);
            Assert.Equal(absent.Error, invisible.Error);
            Assert.Equal(absent.Message, invisible.Message);
            Assert.Null(invisible.Details);
        }

        [Fact]
        public void Template_UngrantedFolder_IsUniformForbidden()
        {
            var template = BuildStoredTemplate(Guid.NewGuid());

            _authorization.Setup(a => a.Authorize(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var (body, status) = BodyAndStatus(CreateTemplatesController().GetTemplate(template.Id));

            Assert.Equal(403, status);
            Assert.Equal(ManagementApiErrors.ForbiddenCode, body.Error);
            Assert.Contains(ApiTokenOperations.AlertsRead, body.Message, StringComparison.Ordinal);
            Assert.Null(body.Details);
        }

        [Fact]
        public async Task Template_MissingFolderId_IsUniformValidationWithFieldDetails()
        {
            var (body, status) = BodyAndStatus(
                await CreateTemplatesController().CreateTemplate(BuildDto() with { FolderId = Guid.Empty }));

            Assert.Equal(400, status);
            Assert.Equal(ManagementApiErrors.ValidationFailedCode, body.Error);
            Assert.NotNull(body.Details);

            var details = Assert.IsType<Dictionary<string, string[]>>(body.Details);
            Assert.NotEmpty(details["folderId"]);
        }

        [Fact]
        public async Task Template_StructuralErrors_CarryFieldKeyedDetails()
        {
            var (body, status) = BodyAndStatus(
                await CreateTemplatesController().CreateTemplate(BuildDto() with { Name = "", Paths = [] }));

            Assert.Equal(400, status);
            Assert.Equal(ManagementApiErrors.ValidationFailedCode, body.Error);

            var details = Assert.IsType<Dictionary<string, string[]>>(body.Details);
            Assert.NotEmpty(details["name"]);
            Assert.NotEmpty(details["paths"]);
        }

        [Fact]
        public async Task Template_DeleteConflict_IsUniformConflict()
        {
            var template = BuildStoredTemplate(Guid.NewGuid());

            _cache.Setup(c => c.RemoveAlertTemplateAsync(It.IsAny<Guid>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync((false, "partial per-sensor policy removal"));

            var (body, status) = BodyAndStatus(await CreateTemplatesController().DeleteTemplate(template.Id));

            Assert.Equal(409, status);
            Assert.Equal(ManagementApiErrors.ConflictCode, body.Error);
            Assert.Contains("partial per-sensor policy removal", body.Message, StringComparison.Ordinal);
            Assert.Null(body.Details);
        }

        [Fact]
        public void Schedule_DeniedGate_IsUniformForbidden()
        {
            _authorization.Setup(a => a.HasOperationAtAnyVisibleBoundary(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>()))
                .Returns(false);

            var (body, status) = BodyAndStatus(CreateSchedulesController().GetSchedules());

            Assert.Equal(403, status);
            Assert.Equal(ManagementApiErrors.ForbiddenCode, body.Error);
            Assert.Contains(ApiTokenOperations.AlertsRead, body.Message, StringComparison.Ordinal);
            Assert.Null(body.Details);
        }

        [Fact]
        public void Schedule_AbsentId_IsUniformNotFound()
        {
            var (body, status) = BodyAndStatus(CreateSchedulesController().GetSchedule(Guid.NewGuid()));

            Assert.Equal(404, status);
            Assert.Equal(ManagementApiErrors.NotFoundCode, body.Error);
            Assert.NotNull(body.Message);
            Assert.Null(body.Details);
        }


        private AlertTemplateModel BuildStoredTemplate(Guid folderId)
        {
            var dto = BuildDto() with { FolderId = folderId };
            var model = new AlertTemplateModel(AlertTemplateDtoMapper.ToEntity(dto, Guid.NewGuid(),
                new Dictionary<string, Chat>()));

            _cache.Setup(c => c.GetAlertTemplate(model.Id)).Returns(model);
            _cache.Setup(c => c.GetAlertTemplateModels()).Returns([model]);

            return model;
        }
    }
}
