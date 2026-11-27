using System;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Filters;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Model.ManagementApi.Chats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    /// <summary>
    /// The notification chats of the management API: the read surface (epic #1347
    /// follow-up) — the id source for the destination.chats references alert policies and
    /// alert templates validate against — the verification pair (#1826: a test message, a
    /// Telegram invitation), the write surface (#1829: create, update, delete) and the
    /// setup guide (#1831). Bearer-
    /// token authenticated only (the HsmApiToken scheme); served on the web-UI port only.
    /// The token mirrors its owner: the caller-wide gate applies, then a chat is visible
    /// when it is global or bound to a folder the owner can see; invisible chats answer the
    /// same 404 as an unknown id.
    /// </summary>
    // The class attributes are exactly what ManagementApiGuardMiddleware admits
    // into the management area: [ManagementApi] + the HsmApiToken management
    // policy (bearer token only, plain 401 challenge) — this
    // controller never runs under the cookie scheme. Thin on purpose: routing
    // + the uniform contract; the visibility rules live in ChatsReadService,
    // shared with the MCP chat tools (#1393 pattern), the write rules in
    // ChatWriteService on the engine the web editor runs.
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/chats")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class ChatsApiController : ControllerBase
    {
        private readonly ChatsReadService _reader;
        private readonly ChatVerificationService _verification;
        private readonly ChatWriteService _writer;
        private readonly ChatSetupGuideService _guide;

        public ChatsApiController(ChatsReadService reader, ChatVerificationService verification, ChatWriteService writer,
            ChatSetupGuideService guide)
        {
            _reader = reader;
            _verification = verification;
            _writer = writer;
            _guide = guide;
        }


        /// <summary>
        /// The chat setup guide of one channel (#1831): the steps a person performs inside the
        /// messenger, as text, and the calls that do HSM's part of the channel. Answered from
        /// the same source as the web UI's help modals. The server never acts inside the
        /// messengers (ADR docs/adr/0002): a client relays the steps or performs them with its
        /// own tools. Requires the caller-wide gate.
        /// </summary>
        /// <param name="channel">The channel: telegram-bot, telegram-chat, slack or mattermost.</param>
        /// <remarks>
        /// The HSM steps of the web modals — store a webhook, send a test — are no instruction
        /// for a person here: toolSteps names the calls that perform them, each written with the
        /// arguments the channel needs and beside its REST twin (rest). A secret a call takes — a
        /// webhook URL, the bot token — reaches the client from a file or the web UI, never from
        /// the conversation: each such call says so.
        /// </remarks>
        [HttpGet("setup-guide")]
        [ProducesResponseType(typeof(ChatSetupGuideDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetSetupGuide(string channel) =>
            _guide.GetGuide(channel, User).ToActionResult();

        /// <summary>
        /// List chats visible to the token's owner, paginated, ordered by name
        /// then id. Global chats (bound to no folder) and chats bound to a
        /// visible folder are listed; everything else is silently absent, never
        /// a per-item 403. Requires the caller-wide gate (the owner sees at
        /// least one product or folder, or is an admin).
        /// </summary>
        /// <param name="page">1-based page number; clamped into [1, totalPages].</param>
        /// <param name="pageSize">Page size, 1..200 (default 50).</param>
        [HttpGet]
        [ProducesResponseType(typeof(ApiPageDto<ChatDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetChats(int page = 1, int pageSize = ApiPagination.DefaultPageSize) =>
            _reader.ListChats(User, page, pageSize, HttpContext.RequestAborted).ToActionResult();

        /// <summary>Get one chat by id; its folders list is what policy writes validate against.</summary>
        /// <param name="id">Chat id.</param>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetChat(Guid id) =>
            _reader.GetChat(id, User).ToActionResult();

        /// <summary>
        /// Send a test message to one of the chat's channels and answer the delivery outcome
        /// (#1826) — the web test actions answer OK regardless; here the caller learns whether
        /// the message arrived, and nextSteps says what to do with the outcome. Same gate as
        /// the web editor: the chat's detail rule.
        /// </summary>
        /// <param name="id">Chat id.</param>
        /// <param name="request">The channel to test (telegram / slack / mattermost).</param>
        [HttpPost("{id:guid}/test-message")]
        [ProducesResponseType(typeof(ChatTestMessageResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostTestMessage(Guid id, [FromBody] ChatTestMessageRequestDto request) =>
            (await _verification.SendTestMessageAsync(id, request?.Channel, User)).ToActionResult();

        /// <summary>
        /// Issue a ONE-SHOT Telegram invitation for the chat (#1826): the link (direct) or the
        /// group command, its expiry (+10 minutes — the relay through an orchestrator is slower
        /// than the web UI's issue-at-the-moment-of-use) and the person's steps — the chat setup
        /// guide's own words, with this invitation in place. The binding lands
        /// when a person completes /start in Telegram; read the chat's telegramChatId afterwards.
        /// </summary>
        /// <param name="id">Chat id.</param>
        /// <param name="request">The invitation kind (direct / group).</param>
        [HttpPost("{id:guid}/telegram-invitations")]
        [ProducesResponseType(typeof(ChatInvitationDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostTelegramInvitation(Guid id, [FromBody] ChatInvitationRequestDto request) =>
            (await _verification.CreateInvitationAsync(id, request?.Kind, User)).ToActionResult();

        /// <summary>
        /// Create a notification chat (#1829) — ADMIN tokens only: a chat created with an
        /// attacker-supplied webhook URL would exfiltrate every alert sent to it. Optionally
        /// with a Slack and/or Mattermost webhook and folder bindings; a Telegram binding
        /// is added afterwards by a person's /start against an invitation. The 201 echo is
        /// the stored chat — never a webhook URL, only its presence flag — and its nextSteps
        /// name what the setup does next (the test message of each webhook carried, a Telegram
        /// invitation). A client-sent id
        /// is ignored: the id is the server's.
        /// </summary>
        /// <param name="request">The chat to create: name, description, enable switch, aggregation delay, webhooks, folder bindings.</param>
        [HttpPost]
        [ProducesResponseType(typeof(ChatDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateChat([FromBody] CreateChatRequestDto request)
        {
            var result = await _writer.CreateChat(request, User);

            // The Location names the item action of THIS controller: the chats have a GET
            // {id} of their own, so the header needs no other controller's route.
            return result.Success
                ? CreatedAtAction(nameof(GetChat), new { id = result.Value.Id }, result.Value)
                : PolicyWriteResult.ToActionResult(result.Failure);
        }

        /// <summary>
        /// Change a notification chat (#1829) — a partial update: an omitted field does not
        /// change. A webhook or the Telegram binding is removed only by its clear flag; an
        /// empty webhook value never clears one. <c>folders</c> is the complete list of
        /// bindings among the folders the token's owner manages (omitted: unchanged). Admin,
        /// or the manager of a folder the chat is bound to — a global chat is admin-only.
        /// The 200 echo is the stored chat, never a webhook URL; its nextSteps name the test
        /// message of each webhook the request set, and the invitation when the Telegram
        /// binding was cleared.
        /// </summary>
        /// <param name="id">Chat id.</param>
        /// <param name="request">The changes; every field optional.</param>
        [HttpPatch("{id:guid}")]
        [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateChat(Guid id, [FromBody] UpdateChatRequestDto request) =>
            (await _writer.UpdateChat(id, request, User)).ToActionResult();

        /// <summary>
        /// Remove a notification chat (#1829): the same removal the web UI's list runs. Its
        /// id is pruned from every folder it was bound to, and through them from the alert
        /// policies of those folders' products; it is also taken out of the default chats of
        /// every folder that lists it (#1927). References elsewhere are left in place.
        /// Every write leaves a journal record. Admin, or the manager of a
        /// folder the chat is bound to. With the
        /// <c>X-HSM-Dry-Run: true</c> header nothing is removed and the 200 body is the
        /// preview: how many sensors' alerts reference the chat, the folder bindings the
        /// deletion would take with it, and the folders whose default chats list it. Alerts
        /// that referenced the chat outside the folders it was bound to keep their reference
        /// and deliver nothing from now on — point them at another chat.
        /// </summary>
        /// <param name="id">Chat id.</param>
        [HttpDelete("{id:guid}")]
        [DryRunHeader]
        [ProducesResponseType(typeof(ChatDeletePreviewDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteChat(Guid id)
        {
            var result = await _writer.DeleteChat(id, User, Request.WriteOptions().DryRun);

            if (result.IsDryRun)
                return Ok(result.Preview);

            return result.Success ? NoContent() : PolicyWriteResult.ToActionResult(result.Failure);
        }
    }
}
