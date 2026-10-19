using HSMServer.Authentication;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Mcp;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using Moq;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;
using System.Linq;

namespace HSMServer.Core.Tests.Mcp
{
    // Contract-level tests of the change-set MCP tools (#1570): delegation to
    // the shared engine with the ambient principal, uniform error rendering,
    // and the paging envelope of list_change_sets. The engine's own semantics
    // live in ChangeSetRollbackTests; the wire surface in HsmMcpWireTests.
    public sealed class ChangeSetsMcpToolsTests
    {
        private readonly Mock<IChangeSetAdministrationService> _engine = new();

        private readonly IHttpContextAccessor _accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
        };


        [Fact]
        public async Task ListChangeSets_DelegatesWithFiltersAndPagesTheEnvelope()
        {
            var summaries = Enumerable.Range(0, 3)
                .Select(i => new ChangeSetSummaryDto { Id = Guid.NewGuid().ToString(), Author = "alice" })
                .ToList();

            _engine.Setup(e => e.ListChangeSets(It.IsAny<ClaimsPrincipal>(), It.IsAny<ChangeSetFilter>()))
                .Returns(PolicyWriteResult<List<ChangeSetSummaryDto>>.Ok(summaries));

            var result = await CreateTools().ListChangeSetsAsync(author: "alice", limit: 2, offset: 1);

            Assert.Equal(2, result.ChangeSets.Count);
            Assert.Equal(3, result.TotalFound);
            Assert.Equal(2, result.Limit);
            Assert.Equal(1, result.Offset);

            _engine.Verify(e => e.ListChangeSets(It.IsAny<ClaimsPrincipal>(),
                It.Is<ChangeSetFilter>(f => f.Author == "alice")), Times.Once);
        }

        [Fact]
        public async Task GetChangeSet_DelegatesWithTheAmbientPrincipal()
        {
            var details = new ChangeSetDetailsDto
            {
                Summary = new ChangeSetSummaryDto { Id = Guid.NewGuid().ToString() },
                Entries = [],
            };

            _engine.Setup(e => e.GetChangeSet(It.IsAny<Guid>(), It.IsAny<ClaimsPrincipal>()))
                .Returns(PolicyWriteResult<ChangeSetDetailsDto>.Ok(details));

            var result = await CreateTools().GetChangeSetAsync(Guid.Parse(details.Summary.Id));

            Assert.Same(details, result);
        }

        [Fact]
        public async Task RollbackChangeSet_DelegatesForceAndDryRun()
        {
            var rollback = new RollbackResultDto { ChangeSetId = Guid.NewGuid().ToString(), Status = "completed" };

            _engine.Setup(e => e.RollbackAsync(It.IsAny<Guid>(), It.IsAny<ClaimsPrincipal>(), true, false))
                .ReturnsAsync(PolicyWriteResult<RollbackResultDto>.Ok(rollback));

            var result = await CreateTools().RollbackChangeSetAsync(Guid.Parse(rollback.ChangeSetId), force: true);

            Assert.Equal("completed", result.Status);
        }

        [Fact]
        public async Task RollbackChangeSet_NotFound_RendersTheUniformToolError()
        {
            _engine.Setup(e => e.RollbackAsync(It.IsAny<Guid>(), It.IsAny<ClaimsPrincipal>(), false, false))
                .ReturnsAsync(PolicyWriteResult<RollbackResultDto>.Fail(PolicyWriteOutcome.NotFound));

            var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                () => CreateTools().RollbackChangeSetAsync(Guid.NewGuid()));

            Assert.Contains("The requested resource was not found.", error.Message, StringComparison.Ordinal);
        }


        private ChangeSetsMcpTools CreateTools() => new(_engine.Object, _accessor);

        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));
    }
}
