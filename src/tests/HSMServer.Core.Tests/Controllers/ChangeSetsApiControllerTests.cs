using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Moq;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // Contract-level tests of the change-set REST surface (#1570): the
    // uniform error contract rendering, the rollback header echo, and the
    // area's controller conventions (management-area metadata, route, bearer
    // policy). Decisions live in the service suite (ChangeSetRollbackTests).
    public sealed class ChangeSetsApiControllerTests
    {
        private readonly Mock<IChangeSetAdministrationService> _service = new();

        private readonly IHttpContextAccessor _accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
        };


        [Fact]
        public void ListChangeSets_RendersTheSummaries()
        {
            var summaries = new List<ChangeSetSummaryDto>
            {
                new() { Id = Guid.NewGuid().ToString(), Author = "alice", Status = "applied" },
            };

            _service.Setup(s => s.ListChangeSets(It.IsAny<ClaimsPrincipal>(), It.IsAny<ChangeSetFilter>(), null))
                .Returns(PolicyWriteResult<List<ChangeSetSummaryDto>>.Ok(summaries));

            var result = CreateController().ListChangeSets(author: "alice");

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Same(summaries, ok.Value);

            _service.Verify(s => s.ListChangeSets(It.IsAny<ClaimsPrincipal>(),
                It.Is<ChangeSetFilter>(f => f.Author == "alice" && f.Limit == 50), null), Times.Once);
        }

        [Fact]
        public void GetChangeSet_NotFound_RendersTheUniform404()
        {
            _service.Setup(s => s.GetChangeSet(It.IsAny<Guid>(), It.IsAny<ClaimsPrincipal>(), null))
                .Returns(PolicyWriteResult<ChangeSetDetailsDto>.Fail(PolicyWriteOutcome.NotFound));

            var (body, status) = BodyAndStatus(CreateController().GetChangeSet(Guid.NewGuid()));

            Assert.Equal(404, status);
            Assert.Equal(ManagementApiErrors.NotFoundCode, body.Error);
            Assert.Null(body.Details);
        }

        [Fact]
        public async Task Rollback_Success_EchoesTheRollbackChangeSetHeader()
        {
            var rollbackChangeSetId = Guid.NewGuid();

            _service.Setup(s => s.RollbackAsync(It.IsAny<Guid>(), It.IsAny<ClaimsPrincipal>(), true, false, null))
                .ReturnsAsync(PolicyWriteResult<RollbackResultDto>.Ok(new RollbackResultDto
                {
                    ChangeSetId = Guid.NewGuid().ToString(),
                    Status = "completed",
                    RollbackChangeSetId = rollbackChangeSetId.ToString(),
                }));

            var result = await CreateController().Rollback(Guid.NewGuid(), force: true);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("completed", ((RollbackResultDto)ok.Value).Status);
            Assert.Equal(rollbackChangeSetId.ToString(),
                _accessor.HttpContext.Response.Headers[ChangeSetHeaders.ChangeSetId].ToString());
        }

        [Fact]
        public async Task Rollback_DryRun_DoesNotEchoTheHeader()
        {
            _service.Setup(s => s.RollbackAsync(It.IsAny<Guid>(), It.IsAny<ClaimsPrincipal>(), false, true, null))
                .ReturnsAsync(PolicyWriteResult<RollbackResultDto>.Ok(new RollbackResultDto
                {
                    ChangeSetId = Guid.NewGuid().ToString(),
                    Status = "completed",
                    DryRun = true,
                }));

            await CreateController().Rollback(Guid.NewGuid(), dryRun: true);

            Assert.False(_accessor.HttpContext.Response.Headers.ContainsKey(ChangeSetHeaders.ChangeSetId));
        }

        [Fact]
        public void Rollback_Conflict_RendersTheUniform409()
        {
            _service.Setup(s => s.RollbackAsync(It.IsAny<Guid>(), It.IsAny<ClaimsPrincipal>(), false, false, null))
                .ReturnsAsync(PolicyWriteResult<RollbackResultDto>.Fail(PolicyWriteOutcome.Conflict,
                    message: "entities changed",
                    details: new Dictionary<string, string> { ["ProductA/cpu"] = "policy added" }));

            var (conflictBody, conflictStatus) = BodyAndStatus(CreateController().Rollback(Guid.NewGuid()).Result);

            Assert.Equal(409, conflictStatus);
            Assert.Equal(ManagementApiErrors.ConflictCode, conflictBody.Error);
            Assert.Equal("policy added", conflictBody.Details["ProductA/cpu"]);
        }


        // === Conventions pins (the area's metadata contract) ===

        [Fact]
        public void Controller_ClassCarriesManagementAreaMetadata()
        {
            var type = typeof(ChangeSetsApiController);

            Assert.True(type.IsDefined(typeof(ApiControllerAttribute), false));
            Assert.True(type.IsDefined(typeof(ManagementApiAttribute), false));
            Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>() ??
                           throw new InvalidOperationException("missing Authorize"));
            Assert.Equal("HsmApiTokenManagement",
                type.GetCustomAttribute<AuthorizeAttribute>().Policy);
            Assert.Equal("api/v1/change-sets",
                type.GetCustomAttribute<RouteAttribute>().Template);
        }


        private static (ManagementApiErrorDto Body, int? Status) BodyAndStatus(IActionResult result) =>
            (Assert.IsType<ManagementApiErrorDto>(Assert.IsType<ObjectResult>(result).Value),
                Assert.IsType<ObjectResult>(result).StatusCode);


        private ChangeSetsApiController CreateController() =>
            new(_service.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = _accessor.HttpContext,
                },
            };

        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));
    }
}
