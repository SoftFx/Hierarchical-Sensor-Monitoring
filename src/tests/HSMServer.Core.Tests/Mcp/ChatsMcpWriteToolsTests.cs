using HSMServer.Authentication;
using HSMServer.Core.Tests.Model.ManagementApi.Chats;
using HSMServer.Mcp;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Mcp
{
    // #1829: the MCP half of the chat write surface — the three tools as renderings of the
    // REAL ChatWriteService over ChatWriteHarness (the semantics are the service's, pinned
    // by ChatWriteServiceTests): the DTO each tool answers, the failure text an agent reads
    // to correct its arguments, the dry-run arm of the delete, and the tool metadata.
    public sealed class ChatsMcpWriteToolsTests
    {
        private readonly ChatWriteHarness _world = new();


        private ChatsMcpTools CreateTools() =>
            new(_world.CreateReader(),
                new ChatVerificationService(_world.Chats.Object, _world.Authorization.Object, _world.Users.Object,
                    new Mock<IChatDelivery>().Object),
                _world.CreateWriter(),
                new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = _world.Token } });


        // ---- create_chat -------------------------------------------------------------


        [Fact]
        public async Task CreateChat_AnswersTheStoredChat_WithoutTheWebhookUrl()
        {
            var chat = await CreateTools().CreateChat(name: "Ops alerts", slackWebhookUrl: ChatWriteHarness.SlackUrl);

            Assert.Equal(_world.Store.Single().Id, chat.Id);
            Assert.Equal("Ops alerts", chat.Name);
            Assert.True(chat.HasSlackWebhook);
            Assert.True(chat.SendMessages);                 // the engine's default
            Assert.Equal(60, chat.MessagesAggregationTimeSec);
            Assert.DoesNotContain(ChatWriteHarness.SlackUrl, JsonSerializer.Serialize(chat));
        }

        [Fact]
        public async Task CreateChat_WithFolders_BindsTheChat_AndAnswersTheBindings()
        {
            var folder = _world.AddFolder();

            var chat = await CreateTools().CreateChat(name: "Ops alerts", folders: [folder.Id]);

            Assert.Equal([folder.Id], chat.Folders);
            Assert.Contains(chat.Id, _world.FolderStore.Single().Chats);
        }

        [Fact]
        public async Task CreateChat_NonAdmin_IsAToolError_CarryingTheRefusalOfTheRestTwin()
        {
            _world.Authorization.Setup(a => a.AuthorizeChatCreate(It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var error = await Assert.ThrowsAsync<McpException>(() => CreateTools().CreateChat(name: "Ops alerts"));

            Assert.Equal(PolicyWriteResult.WriteDeniedMessage, error.Message);
            Assert.Empty(_world.Store);
        }

        [Fact]
        public async Task CreateChat_DuplicateName_IsAToolError_NamingTheField()
        {
            _world.AddChat("Ops alerts");

            var error = await Assert.ThrowsAsync<McpException>(() => CreateTools().CreateChat(name: "OPS ALERTS"));

            Assert.Equal("name: Chat with the same name already exists.", error.Message);
        }

        [Fact]
        public async Task CreateChat_FolderTheOwnerDoesNotManage_IsAToolError_NamingFolders()
        {
            var folder = _world.AddFolder();
            _world.Unmanaged.Add(folder.Id);

            var error = await Assert.ThrowsAsync<McpException>(
                () => CreateTools().CreateChat(name: "Ops alerts", folders: [folder.Id]));

            Assert.Equal($"folders: {ChatWriteHarness.FolderNotManagedMessage}", error.Message);
            Assert.Empty(_world.Store);
        }


        // ---- update_chat -------------------------------------------------------------


        [Fact]
        public async Task UpdateChat_ChangesOnlyTheGivenFields_AndAnswersTheStoredChat()
        {
            var chat = _world.AddChat("Ops alerts");

            var answer = await CreateTools().UpdateChat(chat.Id, description: "on-call", sendMessages: false);

            Assert.Equal(chat.Id, answer.Id);
            Assert.Equal("Ops alerts", answer.Name);        // omitted: unchanged
            Assert.Equal("on-call", answer.Description);
            Assert.False(answer.SendMessages);
        }

        [Fact]
        public async Task UpdateChat_ClearFlags_RemoveTheChannels()
        {
            var chat = _world.AddChat("Ops alerts");
            chat.Update(new ChatUpdate { Id = chat.Id, SlackWebhookUrl = ChatWriteHarness.SlackUrl });

            var answer = await CreateTools().UpdateChat(chat.Id, clearSlackWebhook: true);

            Assert.False(answer.HasSlackWebhook);
            Assert.Null(_world.Store.Single().SlackWebhookUrl);
        }

        [Fact]
        public async Task UpdateChat_UnknownChat_IsAToolError_TheNotFoundText()
        {
            var error = await Assert.ThrowsAsync<McpException>(
                () => CreateTools().UpdateChat(Guid.NewGuid(), description: "on-call"));

            Assert.Equal(ManagementApiErrors.NotFoundMessage, error.Message);
        }

        [Fact]
        public async Task UpdateChat_NegativeDelay_IsAToolError_NamingTheField()
        {
            // A negative delay never flushes the chat's alerts: the engine's range rule
            // reaches the agent under the argument's own name.
            var chat = _world.AddChat("Ops alerts");

            var error = await Assert.ThrowsAsync<McpException>(
                () => CreateTools().UpdateChat(chat.Id, messagesAggregationTimeSec: -1));

            Assert.Equal("messagesAggregationTimeSec: Messages delay must be between 0 and 86400 seconds.", error.Message);
            Assert.Equal(60, _world.Store.Single().MessagesAggregationTimeSec);
        }

        [Theory]
        [InlineData("update_chat")]
        [InlineData("delete_chat")]
        public async Task ChatWriteTools_VisibleChatTheCallerCannotChange_AreToolErrors_CarryingTheRefusal(string toolName)
        {
            // The refusal reaches the agent as the same text the REST twin answers with.
            var chat = _world.AddChat("Ops alerts", global: false);
            _world.RefuseChatWrites(ApiTokenAuthorization.Forbidden);

            var error = toolName == "update_chat"
                ? await Assert.ThrowsAsync<McpException>(() => CreateTools().UpdateChat(chat.Id, description: "on-call"))
                : await Assert.ThrowsAsync<McpException>(() => CreateTools().DeleteChat(chat.Id));

            Assert.Equal(PolicyWriteResult.WriteDeniedMessage, error.Message);
            Assert.Single(_world.Store);
        }


        // ---- delete_chat -------------------------------------------------------------


        [Fact]
        public async Task DeleteChat_Applied_AnswersTheRemovedId()
        {
            var chat = _world.AddChat("Ops alerts");

            var answer = await CreateTools().DeleteChat(chat.Id);

            Assert.Equal(chat.Id, answer.Value.Id);
            Assert.Null(answer.Preview);
            Assert.Empty(_world.Store);
        }

        [Fact]
        public async Task DeleteChat_DryRun_AnswersTheUsagePreview_AndRemovesNothing()
        {
            var chat = _world.AddChat("Ops alerts");
            _world.Usage.Setup(u => u.Compute()).Returns((new Dictionary<Guid, int> { [chat.Id] = 7 }, 0));

            var answer = await CreateTools().DeleteChat(chat.Id, dryRun: true);

            Assert.Null(answer.Value);
            Assert.Equal(chat.Id.ToString(), answer.Preview.EntityId);
            Assert.Equal(7, answer.Preview.SensorUsageCount);
            Assert.Single(_world.Store);
        }

        [Fact]
        public async Task DeleteChat_UnknownChat_IsAToolError_TheNotFoundText()
        {
            var error = await Assert.ThrowsAsync<McpException>(() => CreateTools().DeleteChat(Guid.NewGuid()));

            Assert.Equal(ManagementApiErrors.NotFoundMessage, error.Message);
        }


        // ---- metadata ----------------------------------------------------------------


        [Theory]
        [InlineData("create_chat", false)]
        [InlineData("update_chat", true)]
        [InlineData("delete_chat", false)]
        public void ChatWriteTools_AreWrites_WithTheIdempotenceOfTheirName(string toolName, bool idempotent)
        {
            // A create re-sent after a retry would answer the duplicate-name error (or make
            // a second chat); an update re-sent converges; a delete re-sent answers the
            // not-found error.
            var tool = typeof(ChatsMcpTools).GetMethods()
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
                .Single(attribute => attribute?.Name == toolName);

            Assert.False(tool.ReadOnly);
            Assert.Equal(idempotent, tool.Idempotent);
        }
    }
}
