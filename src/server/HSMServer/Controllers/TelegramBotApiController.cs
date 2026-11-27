using System;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.TelegramBot;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    /// <summary>
    /// The Telegram bot credential of the management API (#1830) — the first server-settings
    /// surface of the area: the status of the bot, and the update that saves and applies a
    /// change in one call. Bearer-token authenticated only (the HsmApiToken scheme).
    /// Administrators only, on both actions — the web Configuration page's rule. The token
    /// itself is write-only and never part of an answer.
    /// </summary>
    // The class attributes are exactly what ManagementApiGuardMiddleware admits into the
    // management area: [ManagementApi] + the HsmApiToken management policy (bearer token
    // only, plain 401 challenge) — this controller never runs under the cookie scheme. Thin
    // on purpose: routing + the uniform contract; the rules live in
    // TelegramBotAdministrationService, shared with the MCP tools. The route names a single
    // settings block, so it is singular and kebab-cased like the area's nested resources.
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/telegram-bot")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class TelegramBotApiController : ControllerBase
    {
        private readonly TelegramBotAdministrationService _service;

        public TelegramBotApiController(TelegramBotAdministrationService service) => _service = service;


        /// <summary>
        /// The status of the Telegram bot credential (#1830): the bot name, whether Telegram
        /// delivery is enabled, whether a token is stored and whether the bot is running.
        /// The token itself is write-only and never returned. Administrators only — a
        /// read-only token of an administrator may call it.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(TelegramBotDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetTelegramBot() =>
            _service.GetStatus(User).ToActionResult();

        /// <summary>
        /// Change the Telegram bot credential (#1830) — a partial update: an omitted field
        /// does not change. A new botToken is checked with Telegram BEFORE anything is saved
        /// (a rejected token changes nothing and the running bot keeps running); a passed
        /// botName must be the bot Telegram reports for the effective token; only a token
        /// change restarts the bot. The 200 body is the resulting status — read its
        /// isRunning after a token change — and its nextSteps name the channel to set up next:
        /// the guide of binding a chat to the bot. The token is write-only and never echoed.
        /// Administrators only.
        /// </summary>
        /// <param name="request">The changes: botName, botToken, isEnabled — every field optional.</param>
        [HttpPatch]
        [ProducesResponseType(typeof(TelegramBotDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateTelegramBot([FromBody] UpdateTelegramBotRequestDto request) =>
            (await _service.UpdateAsync(request, User)).ToActionResult();
    }
}
