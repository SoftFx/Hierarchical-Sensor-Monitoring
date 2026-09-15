using System;
using HSMServer.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    /// <summary>
    /// One tree node — a root product or a nested folder — with its direct children
    /// (#1386). Bearer-token authenticated only (the HsmApiToken scheme); served on
    /// the web-UI port only. Unknown and invisible ids answer the SAME 404 (the
    /// area's anti-enumeration rule).
    /// </summary>
    // Products and folders are one model (ProductModel in the flat _tree index);
    // the evaluator authorizes both through the Product boundary. Sensors are NOT
    // addressable here — their node body is the sensor endpoint itself; a sensor id
    // in this route is an unknown node (plain 404).
    //
    // Since #1391 the read logic lives in SensorTreeReadService, shared with the
    // MCP tools; this controller is the REST rendering of it.
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/nodes")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class NodesApiController : ControllerBase
    {
        private readonly SensorTreeReadService _reader;

        public NodesApiController(SensorTreeReadService reader)
        {
            _reader = reader;
        }


        /// <summary>
        /// Get a node (product or folder) by id: metadata plus its DIRECT children
        /// (no recursion). The folders list is paginated (foldersPage/foldersPageSize,
        /// name-ordered); the sensors list is capped at 200 with totalSensors
        /// carrying the uncapped count — the paginated sensor search with
        /// product = this id serves the full sensor list. Folder ids beyond the
        /// first page are reachable ONLY here: walk the pages.
        /// </summary>
        /// <param name="id">Node id (a root product or a nested folder).</param>
        /// <param name="foldersPage">1-based page of the folders list; clamped into [1, totalPages].</param>
        /// <param name="foldersPageSize">Folders page size, 1..200 (default 200 — the single-request ceiling).</param>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(NodeDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetNode(Guid id, int foldersPage = 1,
            int foldersPageSize = SensorTreeDtoMapper.MaxChildrenPerNode) =>
            _reader.GetNode(id, User, foldersPage, foldersPageSize).ToActionResult();
    }
}
