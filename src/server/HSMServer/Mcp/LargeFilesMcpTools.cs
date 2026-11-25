using System;
using System.ComponentModel;
using System.Security.Claims;
using HSMServer.Model.ManagementApi.LargeFiles;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace HSMServer.Mcp
{
    /// <summary>
    /// The large file tool of the MCP surface (#1885): the description of a large file value with the link to its archive
    /// — a thin rendering of <see cref="LargeFileDownloadService.Describe"/>, the same decision as its REST twin
    /// GET /api/v1/sensors/{sensorId}/large-files/{fileId}.
    /// </summary>
    // The bytes never go through MCP: the link is a management API URL the agent fetches with the same bearer token, and
    // the archive opens only with the product's dump password, which no API returns.
    [McpServerToolType]
    public sealed class LargeFilesMcpTools
    {
        private readonly LargeFileDownloadService _downloads;
        private readonly IHttpContextAccessor _http;

        public LargeFilesMcpTools(LargeFileDownloadService downloads, IHttpContextAccessor http)
        {
            _downloads = downloads;
            _http = http;
        }


        [McpServerTool(Name = "get_large_file", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("Describes a large file value of a sensor (a dump or another big file the collector uploaded): name, extension, original size, source time, state ('verified' = kept, not downloadable yet; 'packed' = downloadable), buildState and notReadyReason, and once packed its archive: id, size, sha256 (hex), passwordVersion, passwordSetUtc and downloadUrl. The file's bytes never go through MCP: GET downloadUrl with the same 'Authorization: Bearer' token (byte ranges and resume with If-Range are supported; the answer's Repr-Digest is the archive's SHA-256). The archive is a zip encrypted with AES-256 under the product's dump password, which no API or tool returns: ask the operator for the password of the version the download reports (X-Hsm-Password-Version). A new password re-encrypts the archive under a new id, so an old downloadUrl answers 404: call this tool again. Unknown files, files of another sensor and files the token's owner may not see answer the same error.")]
        public LargeFileDto GetLargeFile(
            [Description("Sensor id the file value belongs to.")] Guid sensorId,
            [Description("Large file id (from the sensor's file value).")] Guid fileId)
        {
            var request = _http.HttpContext?.Request
                ?? throw new ModelContextProtocol.McpException("No HTTP context is available for this tool call.");

            return McpToolErrors.Unwrap(_downloads.Describe(sensorId, fileId, User, $"{request.Scheme}://{request.Host}"));
        }


        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
