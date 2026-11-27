using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Core.Tests.Model.ManagementApi.TelegramBot;
using HSMServer.Mcp;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Model.ManagementApi.TelegramBot;
using HSMServer.Notifications;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Mcp
{
    // #1830: the MCP half of the bot-credential surface — the two tools as renderings of the
    // REAL TelegramBotAdministrationService over TelegramBotHarness (the semantics are the
    // service's, pinned by TelegramBotAdministrationServiceTests): the status DTO, the
    // failure text an agent reads to correct its arguments, and the tool metadata.
    public sealed class TelegramBotMcpToolsTests
    {
        private readonly TelegramBotHarness _world = new();


        private TelegramBotMcpTools CreateTools() =>
            new(_world.CreateService(), new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = _world.Caller } });


        // ---- get_telegram_bot ----------------------------------------------------------


        [Fact]
        public void GetTelegramBot_AnswersTheStatus_WithoutTheToken()
        {
            var status = CreateTools().GetTelegramBot();

            Assert.Equal(new TelegramBotDto { BotName = "hsm_alerts_bot", IsEnabled = true, HasBotToken = true, IsRunning = true }, status);
            Assert.DoesNotContain(TelegramBotHarness.OldToken, JsonSerializer.Serialize(status));
        }


        [Fact]
        public void GetTelegramBot_NonAdmin_IsAToolError_CarryingTheRefusalOfTheRestTwin()
        {
            _world.Authorization.Setup(a => a.AuthorizeServerSettingsRead(It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var error = Assert.Throws<McpException>(() => CreateTools().GetTelegramBot());

            Assert.Equal("The token's owner is not an administrator.", error.Message);
        }


        // ---- update_telegram_bot -------------------------------------------------------


        [Fact]
        public async Task UpdateTelegramBot_SavesAndRestarts_AnswersTheStatus_WithoutTheToken()
        {
            _world.TelegramAnswers(_ => "hsm_oncall_bot");

            var status = await CreateTools().UpdateTelegramBot(botName: "hsm_oncall_bot", botToken: TelegramBotHarness.NewToken);

            Assert.Equal(TelegramBotHarness.NewToken, _world.Telegram.BotToken);
            Assert.Equal(["probe", "save", "restart"], _world.Steps);
            // The status half of the echo, then the next steps it carries (#1831): the next channel.
            Assert.Equal(new TelegramBotDto { BotName = "hsm_oncall_bot", IsEnabled = true, HasBotToken = true, IsRunning = true },
                status with { NextSteps = null });
            Assert.Equal(ChatSetupNextSteps.AfterBotSaved(status), status.NextSteps);
            Assert.Contains("\"nextSteps\"", JsonSerializer.Serialize(status, McpJsonUtilities.DefaultOptions));
            Assert.DoesNotContain(TelegramBotHarness.NewToken, JsonSerializer.Serialize(status, McpJsonUtilities.DefaultOptions));
        }


        [Fact]
        public async Task UpdateTelegramBot_NothingGiven_ChangesNothing()
        {
            var status = await CreateTools().UpdateTelegramBot();

            Assert.Empty(_world.Steps);
            Assert.Equal("hsm_alerts_bot", status.BotName);
        }


        [Fact]
        public async Task UpdateTelegramBot_ATokenTelegramRejects_IsAToolError_NamingTheField()
        {
            _world.TelegramRefuses(_ => TelegramTokenProbe.Rejected("Unauthorized"));

            var error = await Assert.ThrowsAsync<McpException>(
                () => CreateTools().UpdateTelegramBot(botToken: TelegramBotHarness.NewToken));

            Assert.Equal("botToken: Telegram rejected the bot token: Unauthorized.", error.Message);
            Assert.Equal(TelegramBotHarness.OldToken, _world.Telegram.BotToken);
        }


        [Fact]
        public async Task UpdateTelegramBot_ANameOfAnotherBot_IsAToolError_NamingTheBotTelegramReported()
        {
            _world.TelegramAnswers(_ => "hsm_oncall_bot");

            var error = await Assert.ThrowsAsync<McpException>(
                () => CreateTools().UpdateTelegramBot(botName: "typo_bot", botToken: TelegramBotHarness.NewToken));

            Assert.Equal("botName: The bot name does not match the bot of the new token; Telegram reports 'hsm_oncall_bot'.", error.Message);
        }


        [Fact]
        public async Task UpdateTelegramBot_TelegramUnreachable_IsAToolError_TheConflictText()
        {
            _world.TelegramRefuses(_ => TelegramTokenProbe.Unreachable("GET https://api.telegram.org/ timed out"));

            var error = await Assert.ThrowsAsync<McpException>(
                () => CreateTools().UpdateTelegramBot(botToken: TelegramBotHarness.NewToken));

            Assert.Equal("Telegram could not be reached to check the bot token; nothing was changed. Try again later.", error.Message);
        }


        [Fact]
        public async Task UpdateTelegramBot_NonAdmin_IsAToolError_CarryingTheRefusalOfTheRestTwin()
        {
            _world.Authorization.Setup(a => a.AuthorizeServerSettingsWrite(It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var error = await Assert.ThrowsAsync<McpException>(
                () => CreateTools().UpdateTelegramBot(botToken: TelegramBotHarness.NewToken));

            Assert.Equal(PolicyWriteResult.WriteDeniedMessage, error.Message);
            Assert.Empty(_world.Steps);
        }


        // ---- metadata ------------------------------------------------------------------


        [Theory]
        [InlineData("get_telegram_bot", true, true)]
        [InlineData("update_telegram_bot", false, true)]
        public void TelegramBotTools_CarryTheReadOnlyAndTheIdempotenceOfTheirName(string toolName, bool readOnly, bool idempotent)
        {
            // An update that changes nothing restarts nothing, so the same call again
            // converges on the same credential: retry-safe.
            var tool = typeof(TelegramBotMcpTools).GetMethods()
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
                .Single(attribute => attribute?.Name == toolName);

            Assert.Equal(readOnly, tool.ReadOnly);
            Assert.Equal(idempotent, tool.Idempotent);
        }
    }
}
