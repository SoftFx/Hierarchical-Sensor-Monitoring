using System;
using HSMServer.Authentication;
using HSMServer.Model.ManagementApi.LargeFiles;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    /// <summary>
    /// The web download of a large file's archive (ADR-0022 points 14-15, #1885): a signed-in user who may see the
    /// product downloads it; everything else answers the same 404. The decisions live in
    /// <see cref="LargeFileDownloadService"/>; the management API twin is <see cref="LargeFilesApiController"/>.
    /// </summary>
    // Cookie-authenticated like every web page (BaseController's [Authorize]): not signed in redirects to the login page.
    // A GET only: a cross-site page can start a download in the signed-in user's name (it cannot read the bytes, and the
    // download log records it), the same exposure as any download link.
    public sealed class LargeFilesController : BaseController
    {
        private readonly LargeFileDownloadService _downloads;


        public LargeFilesController(IUserManager userManager, LargeFileDownloadService downloads) : base(userManager)
        {
            _downloads = downloads;
        }


        [HttpGet("LargeFiles/Download/{fileId:guid}/{archiveId:guid}")]
        public IActionResult Download(Guid fileId, Guid archiveId)
        {
            var caller = LargeFileCaller.Web(CurrentUser, HttpContext.Connection.RemoteIpAddress?.ToString());
            var decision = _downloads.Decide(fileId, archiveId, caller, record: true);

            return new LargeFileDownloadResult(decision, LargeFileSurface.Web,
                LargeFileDownloadResult.CameThroughTrustedProxy(HttpContext), _downloads.TryOpenArchive);
        }
    }
}
