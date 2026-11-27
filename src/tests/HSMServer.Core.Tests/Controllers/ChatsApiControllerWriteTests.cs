using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Tests.Model.ManagementApi.Chats;
using HSMServer.Filters;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // #1829: the REST half of the chat write surface — a thin rendering of ChatWriteService
    // (the semantics are the service's, pinned by ChatWriteServiceTests): the 201 + Location
    // of a creation, the 200 echo of an update, the 204 — or the dry-run preview behind the
    // X-HSM-Dry-Run header — of a deletion, and the area's error contract for the refusals.
    public sealed class ChatsApiControllerWriteTests
    {
        private readonly ChatWriteHarness _world = new();


        private ChatsApiController CreateController(bool dryRun = false)
        {
            var httpContext = new DefaultHttpContext { User = _world.Token };

            if (dryRun)
                httpContext.Request.Headers[ChangeSetHeaders.DryRun] = "true";

            return new ChatsApiController(_world.CreateReader(),
                new ChatVerificationService(_world.Chats.Object, _world.Authorization.Object, _world.Users.Object,
                    new Mock<IChatDelivery>().Object, new Mock<IServerConfig>().Object),
                _world.CreateWriter(),
                new ChatSetupGuideService(_world.Authorization.Object, new Mock<IServerConfig>().Object))
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
            };
        }

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


        // ---- create -----------------------------------------------------------------


        [Fact]
        public async Task CreateChat_Is201_WithLocationAtTheChat_AndEchoesItWithoutTheWebhookUrl()
        {
            var created = Assert.IsType<CreatedAtActionResult>(await CreateController().CreateChat(new CreateChatRequestDto
            {
                Name = "Ops alerts",
                SlackWebhookUrl = ChatWriteHarness.SlackUrl,
            }));

            Assert.Equal(201, created.StatusCode);
            Assert.Equal(nameof(ChatsApiController.GetChat), created.ActionName);
            var echo = Assert.IsType<ChatDto>(created.Value);
            Assert.Equal(echo.Id, Assert.IsType<Guid>(created.RouteValues["id"]));
            Assert.Equal("Ops alerts", echo.Name);
            Assert.True(echo.HasSlackWebhook);

            // The URL is a secret: it is stored, and nothing the response can carry holds it.
            Assert.DoesNotContain(ChatWriteHarness.SlackUrl, JsonSerializer.Serialize(created));

            // The echo carries the setup's next steps (#1831) — a created chat points at its test.
            Assert.Contains(echo.NextSteps, step => step.Contains($"send_chat_test_message(chatId: \"{echo.Id}\", channel: \"slack\")"));
        }

        [Fact]
        public async Task CreateChat_NonAdmin_Is403Forbidden()
        {
            _world.Authorization.Setup(a => a.AuthorizeChatCreate(It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var result = await CreateController().CreateChat(new CreateChatRequestDto { Name = "Ops alerts" });

            Assert.Equal(403, StatusCodeOf(result));
            Assert.Equal(ManagementApiErrors.ForbiddenCode, ErrorBodyOf(result).Error);
        }

        [Fact]
        public async Task CreateChat_DuplicateName_Is422_AndTheErrorNamesTheField()
        {
            _world.AddChat("Ops alerts");

            var result = await CreateController().CreateChat(new CreateChatRequestDto { Name = "OPS ALERTS" });

            Assert.Equal(422, StatusCodeOf(result));
            var body = ErrorBodyOf(result);
            Assert.Equal(ManagementApiErrors.UnprocessableEntityCode, body.Error);
            Assert.Equal(["Chat with the same name already exists."], Assert.IsType<string[]>(body.Details["name"]));
        }


        // ---- update -----------------------------------------------------------------


        [Fact]
        public async Task UpdateChat_Is200_WithTheStoredEcho()
        {
            var chat = _world.AddChat("Ops alerts");

            var result = await CreateController().UpdateChat(chat.Id, new UpdateChatRequestDto { Description = "on-call" });

            Assert.Equal(200, StatusCodeOf(result));
            Assert.Equal("on-call", Assert.IsType<ChatDto>(BodyOf(result)).Description);
        }

        [Fact]
        public async Task UpdateChat_UnknownChat_Is404()
        {
            var result = await CreateController().UpdateChat(Guid.NewGuid(), new UpdateChatRequestDto { Description = "on-call" });

            Assert.Equal(404, StatusCodeOf(result));
            Assert.Equal(ManagementApiErrors.NotFoundCode, ErrorBodyOf(result).Error);
        }

        [Fact]
        public async Task UpdateChat_NegativeDelay_Is422_AndTheErrorNamesTheField()
        {
            var chat = _world.AddChat("Ops alerts");

            var result = await CreateController().UpdateChat(chat.Id, new UpdateChatRequestDto { MessagesAggregationTimeSec = -1 });

            Assert.Equal(422, StatusCodeOf(result));
            var body = ErrorBodyOf(result);
            Assert.Equal(ManagementApiErrors.UnprocessableEntityCode, body.Error);
            Assert.Equal(["Messages delay must be between 0 and 86400 seconds."], Assert.IsType<string[]>(body.Details["messagesAggregationTimeSec"]));
        }

        [Fact]
        public async Task UpdateChat_VisibleChatTheCallerMayNotChange_Is403_WithTheAreasSingleMessage()
        {
            var chat = _world.AddChat("Ops alerts", global: false);
            _world.RefuseChatWrites(ApiTokenAuthorization.Forbidden);

            var result = await CreateController().UpdateChat(chat.Id, new UpdateChatRequestDto { Description = "on-call" });

            Assert.Equal(403, StatusCodeOf(result));
            var body = ErrorBodyOf(result);
            Assert.Equal(ManagementApiErrors.ForbiddenCode, body.Error);
            Assert.Equal(PolicyWriteResult.WriteDeniedMessage, body.Message);
            Assert.Null(_world.Store.Single().Description);
        }

        [Fact]
        public async Task UpdateChat_OutsideTheCallersSight_AnswersTheSame404_AsAnUnknownChat()
        {
            // Anti-enumeration, at the body level: status, code and message are one.
            var chat = _world.AddChat("Ops alerts", global: false);
            _world.RefuseChatWrites(ApiTokenAuthorization.NotFound);

            var request = new UpdateChatRequestDto { Description = "on-call" };
            var invisible = await CreateController().UpdateChat(chat.Id, request);
            var unknown = await CreateController().UpdateChat(Guid.NewGuid(), request);

            Assert.Equal(StatusCodeOf(unknown), StatusCodeOf(invisible));
            Assert.Equal(ErrorBodyOf(unknown), ErrorBodyOf(invisible));
        }


        // ---- delete -----------------------------------------------------------------


        [Fact]
        public async Task DeleteChat_Is204_AndRemovesTheChat()
        {
            var chat = _world.AddChat("Ops alerts");

            var result = await CreateController().DeleteChat(chat.Id);

            Assert.Equal(204, StatusCodeOf(result));
            Assert.Empty(_world.Store);
        }

        [Fact]
        public async Task DeleteChat_VisibleChatTheCallerMayNotChange_Is403_WithTheAreasSingleMessage()
        {
            var chat = _world.AddChat("Ops alerts", global: false);
            _world.RefuseChatWrites(ApiTokenAuthorization.Forbidden);

            var result = await CreateController().DeleteChat(chat.Id);

            Assert.Equal(403, StatusCodeOf(result));
            var body = ErrorBodyOf(result);
            Assert.Equal(ManagementApiErrors.ForbiddenCode, body.Error);
            Assert.Equal(PolicyWriteResult.WriteDeniedMessage, body.Message);
            Assert.Single(_world.Store);
        }

        [Fact]
        public void DeleteChat_CarriesTheDryRunHeaderMarker()
        {
            // The marker is what publishes X-HSM-Dry-Run in the OpenAPI document
            // (DryRunHeaderSwaggerFilter) — without it the header is invisible to an agent
            // reading only the spec.
            Assert.NotNull(typeof(ChatsApiController).GetMethod(nameof(ChatsApiController.DeleteChat))
                ?.GetCustomAttribute<DryRunHeaderAttribute>());
        }

        [Fact]
        public async Task DeleteChat_WithTheDryRunHeader_Is200Preview_AndRemovesNothing()
        {
            var chat = _world.AddChat("Ops alerts");
            _world.Usage.Setup(u => u.Compute()).Returns((new Dictionary<Guid, int>(), 0));

            var result = await CreateController(dryRun: true).DeleteChat(chat.Id);

            Assert.Equal(200, StatusCodeOf(result));
            var preview = Assert.IsType<ChatDeletePreviewDto>(BodyOf(result));
            Assert.True(preview.DryRun);
            Assert.Equal(chat.Id.ToString(), preview.EntityId);

            // The preview is the dry run's whole answer here, as the envelope's arm is on MCP: it
            // carries the next step itself (#1831) — the REST twin of the MCP line.
            Assert.Equal([$"delete_chat(chatId: \"{chat.Id}\") without dryRun removes it."], preview.NextSteps);
            Assert.Single(_world.Store);
        }

        [Fact]
        public async Task DeleteChat_UnknownChat_Is404()
        {
            var result = await CreateController().DeleteChat(Guid.NewGuid());

            Assert.Equal(404, StatusCodeOf(result));
            Assert.Equal(ManagementApiErrors.NotFoundCode, ErrorBodyOf(result).Error);
        }
    }
}
