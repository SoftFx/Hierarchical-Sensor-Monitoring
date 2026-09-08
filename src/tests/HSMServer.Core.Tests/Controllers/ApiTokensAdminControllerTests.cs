using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HSMServer.Attributes;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Model.ApiTokensAdmin;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // IsAdmin emergency-revoke endpoints (#1356 step 4, PR B): the typed confirmation
    // naming the target, the required sanitized reason, the not-found answer for
    // unknown owners, the always-advance idempotency (zero-token targets included),
    // availability in the degraded modes the lever exists for (tokens disabled,
    // unhealthy generation state), the retryable correlation-id failure answer when
    // the durable generation advance throws, and the journal audit-of-record.
    public class ApiTokensAdminControllerTests
    {
        private static readonly Guid AdminId = Guid.NewGuid();
        private static readonly Guid TargetUserId = Guid.NewGuid();
        private static readonly Guid UnknownUserId = Guid.NewGuid();

        private readonly Mock<IApiTokenManager> _tokens = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<IJournalService> _journal = new();

        private readonly User _admin = new("admin") { Id = AdminId, IsAdmin = true };
        private readonly User _target = new("target.user") { Id = TargetUserId };


        public ApiTokensAdminControllerTests()
        {
            _users.Setup(u => u[TargetUserId]).Returns(_target);

            _tokens.Setup(t => t.CountQuotaEligibleTokens(TargetUserId)).Returns(2);
            _tokens.Setup(t => t.CountQuotaEligibleTokensGlobally()).Returns(5);
            _tokens.Setup(t => t.GetOwnerRevocationGeneration(TargetUserId)).Returns(4L);
            _tokens.Setup(t => t.GlobalRevocationGeneration).Returns(9L);
            _tokens.Setup(t => t.AdvanceOwnerRevocationGeneration(TargetUserId)).Returns(5L);
            _tokens.Setup(t => t.AdvanceGlobalRevocationGeneration()).Returns(10L);
        }


        private ApiTokensAdminController CreateController() =>
            new(_users.Object, _tokens.Object, _journal.Object, NullLogger<ApiTokensAdminController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = _admin },
                },
            };

        private static EmergencyRevokeResponse Revoke(IActionResult result) =>
            Assert.IsType<JsonResult>(result).Value as EmergencyRevokeResponse;

        private static UserTokenSummaryResponse Summary(IActionResult result) =>
            Assert.IsType<JsonResult>(result).Value as UserTokenSummaryResponse;


        // ---- surface contract -----------------------------------------------------

        [Fact]
        public void Surface_IsAdminCookieOnly_WithAntiforgeryOnMutations()
        {
            // Direct invocation bypasses the filter pipeline, so the admin gate is
            // asserted as the surface contract it is: the class-level attribute that
            // AccountController.Users uses, plus the antiforgery filter on every POST.
            Assert.True(typeof(ApiTokensAdminController)
                .IsDefined(typeof(AuthorizeIsAdminAttribute), inherit: false));

            var controllerType = typeof(ApiTokensAdminController);

            foreach (var (action, posts) in new[] { ("RevokeUserTokens", true), ("RevokeAllTokens", true), ("UserTokenSummary", false) })
            {
                var method = controllerType.GetMethod(action, BindingFlags.Public | BindingFlags.Instance);

                Assert.NotNull(method);
                Assert.Equal(posts, method.IsDefined(typeof(HttpPostAttribute), inherit: false));
                Assert.Equal(!posts, method.IsDefined(typeof(HttpGetAttribute), inherit: false));

                // By type name: the attribute lives in the web-app's framework assets,
                // which this test project does not reference at compile time.
                if (posts)
                    Assert.True(method.GetCustomAttributes(inherit: false)
                        .Any(a => a.GetType().Name == "ValidateAntiForgeryTokenAttribute"),
                        $"{action} must require the antiforgery token");
            }
        }


        // ---- user token summary ---------------------------------------------------

        [Fact]
        public void UserTokenSummary_KnownUser_ReturnsLiveCount()
        {
            var summary = Summary(CreateController().UserTokenSummary(TargetUserId));

            Assert.True(summary.Ok);
            Assert.Equal("target.user", summary.UserName);
            Assert.Equal(2, summary.LiveTokens);
        }

        [Fact]
        public void UserTokenSummary_UnknownUser_NotFound()
        {
            var summary = Summary(CreateController().UserTokenSummary(UnknownUserId));

            Assert.Null(summary);
        }


        // ---- revoke-user ----------------------------------------------------------

        [Fact]
        public void RevokeUserTokens_UnknownUser_NotFound_WithoutAdvancing()
        {
            var answer = Revoke(CreateController().RevokeUserTokens(new RevokeUserTokensRequest
            {
                UserId = UnknownUserId,
                Confirmation = "anyone",
                Reason = "incident",
            }));

            Assert.False(answer.Ok);
            Assert.Equal("not_found", answer.Error);
            _tokens.Verify(t => t.AdvanceOwnerRevocationGeneration(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public void RevokeUserTokens_WrongConfirmation_Denied_WithoutAdvancing()
        {
            var answer = Revoke(CreateController().RevokeUserTokens(new RevokeUserTokensRequest
            {
                UserId = TargetUserId,
                Confirmation = "someone.else",
                Reason = "incident",
            }));

            Assert.False(answer.Ok);
            Assert.Equal("invalid_confirmation", answer.Error);
            _tokens.Verify(t => t.AdvanceOwnerRevocationGeneration(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public void RevokeUserTokens_ConfirmationIsCaseInsensitive()
        {
            var answer = Revoke(CreateController().RevokeUserTokens(new RevokeUserTokensRequest
            {
                UserId = TargetUserId,
                Confirmation = "  TARGET.USER  ",
                Reason = "incident",
            }));

            Assert.True(answer.Ok);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t\n")]
        [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567")]
        public void RevokeUserTokens_InvalidReason_Denied_WithoutAdvancing(string reason)
        {
            var answer = Revoke(CreateController().RevokeUserTokens(new RevokeUserTokensRequest
            {
                UserId = TargetUserId,
                Confirmation = "target.user",
                Reason = reason,
            }));

            Assert.False(answer.Ok);
            Assert.Equal("invalid_reason", answer.Error);
            _tokens.Verify(t => t.AdvanceOwnerRevocationGeneration(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public void RevokeUserTokens_Valid_Advances_ReportsAndAudits()
        {
            var answer = Revoke(CreateController().RevokeUserTokens(new RevokeUserTokensRequest
            {
                UserId = TargetUserId,
                Confirmation = "target.user",
                Reason = "leaked in a chat log",
            }));

            Assert.True(answer.Ok);
            Assert.Equal(5L, answer.NewGeneration);
            Assert.Equal(2, answer.AffectedTokens);

            _journal.Verify(j => j.AddRecord(It.Is<JournalRecordModel>(r =>
                r.PropertyName == "Emergency revoke" &&
                r.Path == $"users/target.user ({TargetUserId})" &&
                r.OldValue.Contains("generation 4") && r.OldValue.Contains("2 live token(s)") &&
                r.OldValue.Contains("leaked in a chat log") &&
                r.NewValue == "generation 5" &&
                r.Initiator.Contains("admin"))), Times.Once);
        }

        [Fact]
        public void RevokeUserTokens_ZeroLiveTokens_StillAdvancesAndSucceeds()
        {
            // Idempotent-by-effect: an owner with nothing left to kill is a success —
            // "after this call the owner has no live old tokens" holds trivially.
            _tokens.Setup(t => t.CountQuotaEligibleTokens(TargetUserId)).Returns(0);

            var answer = Revoke(CreateController().RevokeUserTokens(new RevokeUserTokensRequest
            {
                UserId = TargetUserId,
                Confirmation = "target.user",
                Reason = "cleanup",
            }));

            Assert.True(answer.Ok);
            Assert.Equal(0, answer.AffectedTokens);
            _tokens.Verify(t => t.AdvanceOwnerRevocationGeneration(TargetUserId), Times.Once);
        }

        [Fact]
        public void RevokeUserTokens_ControlCharactersInReason_SanitizedForTheAudit()
        {
            var answer = Revoke(CreateController().RevokeUserTokens(new RevokeUserTokensRequest
            {
                UserId = TargetUserId,
                Confirmation = "target.user",
                Reason = "incident\x0001log-forging\tattempt",
            }));

            Assert.True(answer.Ok);
            _journal.Verify(j => j.AddRecord(It.Is<JournalRecordModel>(r =>
                r.OldValue.Contains("incident log-forging attempt") && !r.OldValue.Contains("\x0001"))), Times.Once);
        }

        [Fact]
        public void RevokeUserTokens_ManagerThrows_RetryableAnswerWithCorrelationId()
        {
            // The throw contract: a throw means the revoke did NOT happen. The answer
            // must say so, carry the trace id that locates the NLog record, and never
            // let the exception escape to the global HTML error page.
            _tokens.Setup(t => t.AdvanceOwnerRevocationGeneration(TargetUserId))
                .Throws(new IOException("simulated generation write failure"));

            var answer = Revoke(CreateController().RevokeUserTokens(new RevokeUserTokensRequest
            {
                UserId = TargetUserId,
                Confirmation = "target.user",
                Reason = "incident",
            }));

            Assert.False(answer.Ok);
            Assert.Equal("revoke_failed", answer.Error);
            Assert.False(string.IsNullOrEmpty(answer.CorrelationId));
            Assert.DoesNotContain("simulated", answer.Message, StringComparison.Ordinal);

            _journal.Verify(j => j.AddRecord(It.Is<JournalRecordModel>(r =>
                r.PropertyName == "Emergency revoke (failed)" &&
                r.NewValue.Contains(answer.CorrelationId))), Times.Once);
        }

        [Fact]
        public void RevokeUserTokens_AuditFailure_DoesNotFailTheRevocation()
        {
            // The audit is best-effort AFTER the advance: the generation already moved,
            // so a journaling failure must not turn a completed revoke into an error.
            _journal.Setup(j => j.AddRecord(It.IsAny<JournalRecordModel>()))
                .Throws(new IOException("simulated journal failure"));

            var answer = Revoke(CreateController().RevokeUserTokens(new RevokeUserTokensRequest
            {
                UserId = TargetUserId,
                Confirmation = "target.user",
                Reason = "incident",
            }));

            Assert.True(answer.Ok);
        }


        // ---- revoke-all -----------------------------------------------------------

        [Fact]
        public void RevokeAllTokens_WrongPhrase_Denied_WithoutAdvancing()
        {
            // Ordinal match on purpose: the modal shows the exact phrase, and a
            // case-variant typo must not pass for a deployment-wide revoke.
            var answer = Revoke(CreateController().RevokeAllTokens(new RevokeAllTokensRequest
            {
                Confirmation = "Revoke-all",
                Reason = "incident",
            }));

            Assert.False(answer.Ok);
            Assert.Equal("invalid_confirmation", answer.Error);
            _tokens.Verify(t => t.AdvanceGlobalRevocationGeneration(), Times.Never);
        }

        [Fact]
        public void RevokeAllTokens_Valid_AdvancesGlobally_ReportsAndAudits()
        {
            var answer = Revoke(CreateController().RevokeAllTokens(new RevokeAllTokensRequest
            {
                Confirmation = "  revoke-all  ",
                Reason = "compromised key store",
            }));

            Assert.True(answer.Ok);
            Assert.Equal(10L, answer.NewGeneration);
            Assert.Equal(5, answer.AffectedTokens);

            _tokens.Verify(t => t.AdvanceGlobalRevocationGeneration(), Times.Once);

            _journal.Verify(j => j.AddRecord(It.Is<JournalRecordModel>(r =>
                r.PropertyName == "Emergency revoke" &&
                r.Path == "deployment (all users)" &&
                r.NewValue == "generation 10" &&
                r.OldValue.Contains("5 live token(s)") &&
                r.OldValue.Contains("compromised key store"))), Times.Once);
        }

        [Fact]
        public void RevokeAllTokens_ManagerThrows_RetryableAnswerWithCorrelationId()
        {
            _tokens.Setup(t => t.AdvanceGlobalRevocationGeneration())
                .Throws(new IOException("simulated generation write failure"));

            var answer = Revoke(CreateController().RevokeAllTokens(new RevokeAllTokensRequest
            {
                Confirmation = "revoke-all",
                Reason = "incident",
            }));

            Assert.False(answer.Ok);
            Assert.Equal("revoke_failed", answer.Error);
            Assert.False(string.IsNullOrEmpty(answer.CorrelationId));
        }


        // ---- degraded modes -------------------------------------------------------

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EmergencyRevoke_WorksInEveryDegradedMode(bool healthy)
        {
            // The whole point of the lever: it must answer when the token index is
            // unhealthy (the per-token surface is dead then) — and it takes no config
            // dependency at all, so the kill switch cannot gate it either way.
            _tokens.Setup(t => t.IsGenerationStateHealthy).Returns(healthy);

            var userRevoke = Revoke(CreateController().RevokeUserTokens(new RevokeUserTokensRequest
            {
                UserId = TargetUserId,
                Confirmation = "target.user",
                Reason = "storage damage cleanup",
            }));

            Assert.True(userRevoke.Ok);

            var allRevoke = Revoke(CreateController().RevokeAllTokens(new RevokeAllTokensRequest
            {
                Confirmation = "revoke-all",
                Reason = "storage damage cleanup",
            }));

            Assert.True(allRevoke.Ok);
        }
    }
}
