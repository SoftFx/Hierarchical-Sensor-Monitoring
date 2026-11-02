using HSMServer.Attributes;
using HSMServer.Authentication;
using HSMServer.Model.Agent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;

namespace HSMServer.Controllers
{
    /// <summary>
    /// The Downloads page (#1550): pick a product, then take its Windows agent or Linux probe bundle from
    /// the existing <c>/api/Agent/installer</c> and <c>/api/Agent/linux-installer</c> endpoints. Admin-only,
    /// like those endpoints.
    /// </summary>
    [Authorize]
    [AuthorizeIsAdmin]
    public class DownloadsController : BaseController
    {
        private readonly IDownloadsPageService _downloads;


        public DownloadsController(IUserManager userManager, IDownloadsPageService downloads) : base(userManager)
        {
            _downloads = downloads;
        }


        [HttpGet]
        public IActionResult Index(Guid? productId) =>
            View(_downloads.Build(CurrentUser, Request.Scheme, Request.Host.Host, Request.Host.Port, productId));
    }
}
