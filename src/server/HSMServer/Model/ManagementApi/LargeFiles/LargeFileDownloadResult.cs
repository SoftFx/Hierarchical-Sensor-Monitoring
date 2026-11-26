using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace HSMServer.Model.ManagementApi.LargeFiles
{
    /// <summary>Which surface a download answer renders for: its error answers follow that surface's conventions.</summary>
    public enum LargeFileSurface
    {
        /// <summary>The cookie-authenticated web page: plain text errors.</summary>
        Web,

        /// <summary>The management API: the area's JSON error contract.</summary>
        Api,
    }


    /// <summary>
    /// The answer to a large file download (ADR-0022 point 15): the archive itself with byte ranges, or the instruction
    /// for the bundled Caddy to serve it, or one of the error answers. Every NotFound of a surface is the same answer
    /// (status, body and headers), whatever the cause.
    /// </summary>
    // The hand-over: a request the bundled Caddy forwarded carries X-Hsm-Serve-Archives: 1 (Caddy overwrites any client
    // copy with header_up). It is honoured only on a connection from a configured trusted proxy (the AgentController
    // predicate); otherwise HSM serves the file itself even when the header is present. The instruction is a 503 with
    // X-Hsm-Archive, which Caddy's handle_response turns into its own file_server answer; a client that somehow gets
    // the instruction sees an error, never an empty "complete" file. X-Hsm-Archive is set only here, after the download
    // record was written (the decision is made before this result runs) — no error answer can carry it.
    public sealed class LargeFileDownloadResult : IActionResult
    {
        public const string ServeArchivesHeader = "X-Hsm-Serve-Archives";
        public const string ArchiveHeader = "X-Hsm-Archive";
        public const string PasswordVersionHeader = "X-Hsm-Password-Version";
        public const string ReprDigestHeader = "Repr-Digest";

        public const string ZipContentType = "application/zip";
        public const string CacheControlValue = "private, no-store";

        internal const string WebNotFoundText = "The file was not found.";

        internal const string HandOverText =
            "This answer is an instruction for the bundled Caddy to serve the archive; it was not meant to reach a client. " +
            "If you see it, the proxy in front of HSM is not configured to serve large file archives.";

        private const string TextContentType = "text/plain; charset=utf-8";

        private readonly LargeFileDownloadDecision _decision;
        private readonly LargeFileSurface _surface;
        private readonly bool _cameThroughTrustedProxy;
        private readonly Func<LargeFileArchiveDownload, Stream> _open;


        public LargeFileDownloadResult(LargeFileDownloadDecision decision, LargeFileSurface surface, bool cameThroughTrustedProxy,
            Func<LargeFileArchiveDownload, Stream> open)
        {
            _decision = decision ?? throw new ArgumentNullException(nameof(decision));
            _surface = surface;
            _cameThroughTrustedProxy = cameThroughTrustedProxy;
            _open = open ?? throw new ArgumentNullException(nameof(open));
        }


        /// <summary>
        /// True when the request reached HSM through a configured trusted proxy: the forwarded-headers middleware set
        /// X-Original-For while it applied the proxy's X-Forwarded-For (the AgentController predicate, #1427).
        /// </summary>
        public static bool CameThroughTrustedProxy(HttpRequest request, IServerConfig config) =>
            config?.Kestrel?.TrustedProxies is { Length: > 0 } &&
            request.Headers.ContainsKey(ForwardedHeadersDefaults.XOriginalForHeaderName);


        /// <summary>The single NotFound answer of a surface.</summary>
        public static IActionResult NotFound(LargeFileSurface surface) =>
            surface == LargeFileSurface.Api
                ? ManagementApiErrors.NotFound()
                : Text(StatusCodes.Status404NotFound, WebNotFoundText);


        public Task ExecuteResultAsync(ActionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            return _decision.Outcome switch
            {
                LargeFileDownloadOutcome.Ok => ServeAsync(context, _decision.Download),
                LargeFileDownloadOutcome.NotReady => Error(StatusCodes.Status409Conflict, _decision.Reason).ExecuteResultAsync(context),
                LargeFileDownloadOutcome.LogUnavailable => Error(StatusCodes.Status503ServiceUnavailable, _decision.Reason).ExecuteResultAsync(context),
                _ => NotFound(_surface).ExecuteResultAsync(context),
            };
        }


        private async Task ServeAsync(ActionContext context, LargeFileArchiveDownload download)
        {
            var http = context.HttpContext;

            if (_cameThroughTrustedProxy && http.Request.Headers[ServeArchivesHeader] == "1")
            {
                var response = http.Response;

                SetArchiveHeaders(response, download);
                response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                response.ContentType = TextContentType;

                // The last header of the answer, after the record (see the class comment); the body follows it.
                response.Headers[ArchiveHeader] = download.RelativePath;

                await response.WriteAsync(HandOverText, http.RequestAborted);
                return;
            }

            // Opened BEFORE any header is set: an archive replaced meanwhile answers the plain NotFound of the surface.
            var stream = _open(download);

            if (stream is null)
            {
                await NotFound(_surface).ExecuteResultAsync(context);
                return;
            }

            SetArchiveHeaders(http.Response, download);

            var file = new FileStreamResult(stream, ZipContentType)
            {
                EnableRangeProcessing = true,
                EntityTag = new EntityTagHeaderValue($"\"{download.ArchiveId:N}\""),
                LastModified = download.LastModifiedUtc is { } modified ? new DateTimeOffset(DateTime.SpecifyKind(modified, DateTimeKind.Utc)) : null,
            };

            await file.ExecuteResultAsync(context);
        }

        private IActionResult Error(int statusCode, string message)
        {
            if (_surface == LargeFileSurface.Web)
                return Text(statusCode, message);

            return statusCode == StatusCodes.Status409Conflict
                ? ManagementApiErrors.Conflict(message)
                : ManagementApiErrors.Unavailable(message);
        }

        private static void SetArchiveHeaders(HttpResponse response, LargeFileArchiveDownload download)
        {
            response.Headers[HeaderNames.ContentDisposition] = LargeFileNames.ContentDisposition(download.FileName);
            response.Headers[ReprDigestHeader] = $"sha-256=:{Convert.ToBase64String(download.Sha256)}:";
            // Absent, not empty, for an archive that is not secure: it opens without a password.
            if (download.PasswordVersion is int version)
                response.Headers[PasswordVersionHeader] = version.ToString(CultureInfo.InvariantCulture);
            response.Headers[HeaderNames.CacheControl] = CacheControlValue;
        }

        private static ContentResult Text(int statusCode, string text) => new()
        {
            StatusCode = statusCode,
            Content = text,
            ContentType = TextContentType,
        };
    }
}
