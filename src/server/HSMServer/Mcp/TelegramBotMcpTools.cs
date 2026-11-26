using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Security.Claims;
using HSMServer.Model.ManagementApi.TelegramBot;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace HSMServer.Mcp
{
    /// <summary>
    /// The Telegram bot credential tools of the MCP surface (#1830) — the first
    /// server-settings tools: a thin rendering of <see cref="TelegramBotAdministrationService"/>,
    /// the engine the REST controller runs. Administrators only.
    /// </summary>
    // The same bearer token and the same DTOs as the REST surface; expected failures
    // surface as tool errors (isError) for the calling agent to self-correct, never as
    // protocol-level crashes. The bot token travels as an argument — an admin hands it over
    // from BotFather — and is answered, journaled and logged as a state, never as a value.
    [McpServerToolType]
    public sealed class TelegramBotMcpTools
    {
        private readonly TelegramBotAdministrationService _service;
        private readonly IHttpContextAccessor _http;

        public TelegramBotMcpTools(TelegramBotAdministrationService service, IHttpContextAccessor http)
        {
            _service = service;
            _http = http;
        }


        [McpServerTool(Name = "get_telegram_bot", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("Gets the status of the Telegram bot — the first server-settings tool: the bot's username (invitation links are built from it), whether Telegram delivery is enabled, whether a bot token is stored and whether the bot is running. ADMIN tokens only. The bot token itself is a secret and never returned: `hasBotToken` says whether one is stored. `isRunning: false` with a token stored means the bot is stopped — the web Configuration page's Restart button starts it; here only a token change does.")]
        public TelegramBotDto GetTelegramBot() =>
            McpToolErrors.Unwrap(_service.GetStatus(User));

        // Idempotent=true: the state-setting write — the same call again changes nothing,
        // and an update that changes nothing restarts nothing.
        [McpServerTool(Name = "update_telegram_bot", ReadOnly = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("Saves and applies a change of the Telegram bot credential in one call — ADMIN tokens only; every argument optional, an omitted one stays unchanged. A new `botToken` is checked with Telegram BEFORE anything is saved: a rejected token changes nothing and the running bot keeps running. A token change is the only update that restarts the bot (`botName` and `isEnabled` are read live). The token is write-only: never returned, never logged. Read `isRunning` of the answer after a token change: false there means the token was saved but the bot did not start.")]
        public async Task<TelegramBotDto> UpdateTelegramBot(
            [Description(TelegramBotDocs.NewBotName)] string botName = null,
            [Description(TelegramBotDocs.NewBotToken)] string botToken = null,
            [Description(TelegramBotDocs.IsEnabled)] bool? isEnabled = null) =>
            McpToolWriteErrors.Unwrap(await _service.UpdateAsync(new UpdateTelegramBotRequestDto
            {
                BotName = botName,
                BotToken = botToken,
                IsEnabled = isEnabled,
            }, User));


        // The shared ambient-principal accessor (see McpToolContext); a property so the
        // tool bodies read like their REST twin's `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
