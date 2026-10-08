using System;
using HSMServer.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    /// <summary>
    /// Read-only surface for notification chats (epic #1347 follow-up): the id
    /// source for the destination.chats references alert policies and alert
    /// templates validate against — without it an API-only client cannot
    /// discover a single valid chat id. Bearer-token authenticated only (the
    /// HsmApiToken scheme); served on the web-UI port only. The token mirrors
    /// its owner: the caller-wide gate applies, then a chat is visible when it
    /// is global or bound to a folder the owner can see; invisible chats
    /// answer the same 404 as an unknown id.
    /// </summary>
    // The class attributes are exactly what ManagementApiGuardMiddleware admits
    // into the management area: [ManagementApi] + the HsmApiToken management
    // policy (bearer token only, SitePort only, plain 401 challenge) — this
    // controller never runs under the cookie scheme. Thin on purpose: routing
    // + the uniform contract; the visibility rules live in ChatsReadService,
    // shared with the MCP chat tools (#1393 pattern).
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/chats")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class ChatsApiController : ControllerBase
    {
        private readonly ChatsReadService _reader;

        public ChatsApiController(ChatsReadService reader) => _reader = reader;


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
    }
}
