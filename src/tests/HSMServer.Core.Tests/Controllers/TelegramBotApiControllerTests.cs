using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Journal;
using HSMServer.Core.Tests.Model.ManagementApi.TelegramBot;
using HSMServer.Mcp;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Model.ManagementApi.TelegramBot;
using HSMServer.Notifications;
using HSMServer.ServerConfiguration;
using HSMServer.ServiceExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // #1830: the REST half of the bot-credential surface — a thin rendering of
    // TelegramBotAdministrationService (the semantics are the service's, pinned by
    // TelegramBotAdministrationServiceTests): the status, the 200 echo of an update, and the
    // area's error contract for the refusals. No body of this surface carries the token.
    public sealed class TelegramBotApiControllerTests
    {
        private readonly TelegramBotHarness _world = new();


        private TelegramBotApiController CreateController() =>
            new(_world.CreateService())
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = _world.Caller } },
            };


        private static int StatusCodeOf(IActionResult result) =>
            result switch
            {
                ObjectResult objectResult => objectResult.StatusCode ?? throw new InvalidOperationException("no status"),
                StatusCodeResult codeResult => codeResult.StatusCode,
                _ => throw new InvalidOperationException($"unexpected result type {result.GetType().Name}"),
            };

        // The body of a result that carries one (OkObjectResult and the error results are
        // ObjectResults too).
        private static object BodyOf(IActionResult result) => Assert.IsAssignableFrom<ObjectResult>(result).Value;

        private static ManagementApiErrorDto ErrorBodyOf(IActionResult result) =>
            Assert.IsType<ManagementApiErrorDto>(BodyOf(result));

        private static string WireOf(IActionResult result) =>
            JsonSerializer.Serialize(BodyOf(result));


        [Fact]
        public void Controller_ClassCarriesManagementAreaMetadata()
        {
            var type = typeof(TelegramBotApiController);

            Assert.NotNull(type.GetCustomAttribute<ManagementApiAttribute>());
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());

            var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorize);
            Assert.Equal(HsmApiTokenDefaults.ManagementPolicy, authorize.Policy);

            // One settings block, not a collection: singular, kebab-cased like the area's
            // nested resources (test-message, telegram-invitations, ttl-policies).
            Assert.Equal("api/v1/telegram-bot", type.GetCustomAttribute<RouteAttribute>()?.Template);

            Assert.True(typeof(ControllerBase).IsAssignableFrom(type));
            Assert.False(typeof(BaseController).IsAssignableFrom(type));
        }


        // ---- the status ---------------------------------------------------------------


        [Fact]
        public void GetTelegramBot_Is200_WithTheStatus_WithoutTheToken()
        {
            var result = CreateController().GetTelegramBot();

            Assert.Equal(200, StatusCodeOf(result));
            Assert.Equal(
                new TelegramBotDto { BotName = "hsm_alerts_bot", IsEnabled = true, HasBotToken = true, IsRunning = true },
                Assert.IsType<TelegramBotDto>(BodyOf(result)));
            Assert.DoesNotContain(TelegramBotHarness.OldToken, WireOf(result));

            // A status read carries no next steps, not even as a null: only a write's answer says
            // what comes after it (#1831).
            Assert.DoesNotContain("NextSteps", WireOf(result));
        }


        [Fact]
        public void GetTelegramBot_NonAdmin_Is403()
        {
            _world.Authorization.Setup(a => a.AuthorizeServerSettingsRead(It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var result = CreateController().GetTelegramBot();

            Assert.Equal(403, StatusCodeOf(result));
            Assert.Equal(ManagementApiErrors.ForbiddenCode, ErrorBodyOf(result).Error);
        }


        // ---- the update -----------------------------------------------------------------


        [Fact]
        public async Task UpdateTelegramBot_Is200_WithTheStatusAfterTheSave()
        {
            _world.TelegramAnswers(_ => "hsm_oncall_bot");

            var result = await CreateController().UpdateTelegramBot(
                new UpdateTelegramBotRequestDto { BotName = "hsm_oncall_bot", BotToken = TelegramBotHarness.NewToken });

            Assert.Equal(200, StatusCodeOf(result));
            Assert.Equal(TelegramBotHarness.NewToken, _world.Telegram.BotToken);

            // The status half of the echo, then the next steps it carries (#1831): the next channel.
            var echo = Assert.IsType<TelegramBotDto>(BodyOf(result));
            Assert.Equal(new TelegramBotDto { BotName = "hsm_oncall_bot", IsEnabled = true, HasBotToken = true, IsRunning = true },
                echo with { NextSteps = null });
            Assert.Equal(ChatSetupNextSteps.AfterBotSaved(echo), echo.NextSteps);
            Assert.Contains("\"NextSteps\"", WireOf(result));
            Assert.DoesNotContain(TelegramBotHarness.NewToken, WireOf(result));
        }


        [Fact]
        public async Task UpdateTelegramBot_NonAdmin_Is403_WithTheAreasSingleMessage()
        {
            _world.Authorization.Setup(a => a.AuthorizeServerSettingsWrite(It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var result = await CreateController().UpdateTelegramBot(new UpdateTelegramBotRequestDto { BotToken = TelegramBotHarness.NewToken });

            Assert.Equal(403, StatusCodeOf(result));
            var body = ErrorBodyOf(result);
            Assert.Equal(ManagementApiErrors.ForbiddenCode, body.Error);
            Assert.Equal(PolicyWriteResult.WriteDeniedMessage, body.Message);
            Assert.Equal(TelegramBotHarness.OldToken, _world.Telegram.BotToken);
        }


        [Fact]
        public async Task UpdateTelegramBot_ARefusedToken_Is422_AndTheErrorNamesTheField()
        {
            _world.TelegramRefuses(_ => TelegramTokenProbe.Rejected("Unauthorized"));

            var result = await CreateController().UpdateTelegramBot(new UpdateTelegramBotRequestDto { BotToken = TelegramBotHarness.NewToken });

            Assert.Equal(422, StatusCodeOf(result));
            var body = ErrorBodyOf(result);
            Assert.Equal(ManagementApiErrors.UnprocessableEntityCode, body.Error);
            Assert.Equal(["Telegram rejected the bot token: Unauthorized."], Assert.IsType<string[]>(body.Details["botToken"]));
            Assert.Equal(TelegramBotHarness.OldToken, _world.Telegram.BotToken);
        }


        [Fact]
        public async Task UpdateTelegramBot_TelegramUnreachable_Is409_WithTheFixedMessage()
        {
            _world.TelegramRefuses(_ => TelegramTokenProbe.Unreachable("GET https://api.telegram.org/ timed out"));

            var result = await CreateController().UpdateTelegramBot(new UpdateTelegramBotRequestDto { BotToken = TelegramBotHarness.NewToken });

            Assert.Equal(409, StatusCodeOf(result));
            var body = ErrorBodyOf(result);
            Assert.Equal(ManagementApiErrors.ConflictCode, body.Error);
            Assert.Equal("Telegram could not be reached to check the bot token; nothing was changed. Try again later.", body.Message);
        }


        // === The DI composition is load-bearing (the #1841 review's P0 on the chat surface) ===

        // The service asks for the ITelegramBotRuntime SEAM, so the registration must map
        // that interface to the NotificationsCenter adapter: a concrete-only registration
        // would leave it unresolvable, and every action and tool of this surface would fail.
        // The provider below is the real AddApplicationServices registration (which also
        // carries AddHsmMcpServer's tool types); the runtime — it would build the live bot —
        // and the journal service — it would open the real LevelDB — are replaced by mocks.
        [Fact]
        public void AddApplicationServices_ResolvesTheBotCredentialSurfaceThroughTheRuntimeSeam()
        {
            // AddApplicationServices registers config.ApiTokens as an instance at
            // registration time — it must be non-null (everything else is only resolved
            // lazily and is replaced below anyway).
            var config = new Mock<IServerConfig>();
            config.Setup(c => c.ApiTokens).Returns(new ApiTokensConfig());

            var services = new ServiceCollection().AddApplicationServices(config.Object);

            // The pin itself: the seam is registered, and it maps to the adapter over
            // NotificationsCenter's concrete bot.
            Assert.Equal(typeof(NotificationsCenterTelegramBotRuntime),
                services.FirstOrDefault(d => d.ServiceType == typeof(ITelegramBotRuntime))?.ImplementationType);

            services.Replace(ServiceDescriptor.Scoped<ITelegramBotRuntime>(_ => new Mock<ITelegramBotRuntime>().Object));
            services.Replace(ServiceDescriptor.Singleton(new Mock<IJournalService>().Object));
            services.Replace(ServiceDescriptor.Singleton(new Mock<IUserManager>().Object));
            services.Replace(ServiceDescriptor.Singleton(new Mock<IApiTokenAuthorizationService>().Object));

            using var provider = services.BuildServiceProvider();

            Assert.NotNull(provider.GetRequiredService<TelegramBotAdministrationService>());

            // MVC builds controllers through the type activator over the request provider,
            // and the MCP SDK constructs tool instances the same way in the request scope
            // (WithTools reflects over the type; it does not register the class itself as a
            // service) — so both are resolved through ActivatorUtilities here, the
            // construction path each transport really takes.
            Assert.NotNull(ActivatorUtilities.CreateInstance<TelegramBotApiController>(provider));
            Assert.NotNull(ActivatorUtilities.CreateInstance<TelegramBotMcpTools>(provider));
        }
    }
}
