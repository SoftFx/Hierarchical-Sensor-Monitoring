using System;
using HSMServer.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Folders;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    /// <summary>
    /// Read-only surface for access-grouping folders (epic #1347 follow-up):
    /// the folderId source for alert-template writes — the entity that scopes
    /// templates and binds notification chats. NOT the tree's nested products
    /// (`GET /api/v1/nodes` serves those). Bearer-token authenticated only
    /// (the HsmApiToken scheme); served on the web-UI port only. The token
    /// mirrors its owner: only folders visible to the owner are listed, and
    /// an invisible folder answers the same 404 as an unknown id.
    /// </summary>
    // The class attributes are exactly what ManagementApiGuardMiddleware admits
    // into the management area (see ChatsApiController). Thin on purpose:
    // routing + the uniform contract; the visibility rules live in
    // FoldersReadService, shared with the MCP folder tools (#1393 pattern).
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/folders")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class FoldersApiController : ControllerBase
    {
        private readonly FoldersReadService _reader;

        public FoldersApiController(FoldersReadService reader) => _reader = reader;


        /// <summary>
        /// List folders visible to the token's owner, paginated, ordered by
        /// name then id. Out-of-sight folders are silently absent, never a
        /// per-item 403; an owner who sees nothing gets an empty list.
        /// </summary>
        /// <param name="page">1-based page number; clamped into [1, totalPages].</param>
        /// <param name="pageSize">Page size, 1..200 (default 50).</param>
        [HttpGet]
        [ProducesResponseType(typeof(ApiPageDto<FolderDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetFolders(int page = 1, int pageSize = ApiPagination.DefaultPageSize) =>
            Ok(_reader.ListFolders(User, page, pageSize, HttpContext.RequestAborted));

        /// <summary>Get one folder by id — the discovery target for template folderId references.</summary>
        /// <param name="id">Folder id.</param>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(FolderDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetFolder(Guid id) =>
            _reader.GetFolder(id, User).ToActionResult();
    }
}
