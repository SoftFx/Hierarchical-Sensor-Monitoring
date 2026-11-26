using System;
using HSMServer.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.LargeFiles;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    /// <summary>
    /// Large file values of the management API (ADR-0022 points 13-15, #1885): the description of a file with the link to
    /// its archive, and the archive download. A token downloads what its owner may see; an access key never downloads.
    /// Unknown, invisible, in-upload and replaced archives answer the SAME 404. The archive is a zip encrypted with the
    /// product's dump password, which no API returns: the operator gives it.
    /// </summary>
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class LargeFilesApiController : ControllerBase
    {
        private readonly LargeFileDownloadService _downloads;
        private readonly IServerConfig _config;


        public LargeFilesApiController(LargeFileDownloadService downloads, IServerConfig config)
        {
            _downloads = downloads;
            _config = config;
        }


        /// <summary>
        /// Describe a large file value of a sensor: name, sizes, state and, once packed, its archive (id, size, SHA-256,
        /// password version and the download link). The dump password is never returned. 404 for an unknown file, a file
        /// of another sensor, a file still being uploaded, or one the token's owner may not see.
        /// </summary>
        /// <param name="sensorId">Sensor id.</param>
        /// <param name="fileId">Large file id (from the sensor's file value).</param>
        [HttpGet("sensors/{sensorId:guid}/large-files/{fileId:guid}")]
        [ProducesResponseType(typeof(LargeFileDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetLargeFile(Guid sensorId, Guid fileId) =>
            _downloads.Describe(sensorId, fileId, User, $"{Request.Scheme}://{Request.Host}").ToActionResult();


        /// <summary>
        /// Download the archive of a large file: a zip with one entry, AES-256 under the product's dump password (open it
        /// with 7-Zip or WinRAR; the archiver built into Windows usually cannot). Byte ranges and resumption (If-Range with
        /// the ETag) are supported. The answer carries Repr-Digest (SHA-256 of the archive) and X-Hsm-Password-Version.
        /// A GET is written to the download log first and refused with 503 when it cannot be; a HEAD is not recorded.
        /// 409 when the file is visible but has no archive yet (the message says why); 404 for anything the token's owner
        /// may not see, and for an archive replaced by a new password (fetch the description again for the new link).
        /// </summary>
        /// <param name="fileId">Large file id.</param>
        /// <param name="archiveId">Archive id from the file's description.</param>
        [HttpGet("large-files/{fileId:guid}/archives/{archiveId:guid}")]
        [HttpHead("large-files/{fileId:guid}/archives/{archiveId:guid}")]
        [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, LargeFileDownloadResult.ZipContentType)]
        [ProducesResponseType(StatusCodes.Status206PartialContent)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status503ServiceUnavailable)]
        public IActionResult DownloadArchive(Guid fileId, Guid archiveId)
        {
            var caller = LargeFileCaller.Token(User, HttpContext.Connection.RemoteIpAddress?.ToString());
            var decision = _downloads.Decide(fileId, archiveId, caller, record: !HttpMethods.IsHead(Request.Method));

            return new LargeFileDownloadResult(decision, LargeFileSurface.Api,
                LargeFileDownloadResult.CameThroughTrustedProxy(Request, _config), _downloads.TryOpenArchive);
        }
    }
}
