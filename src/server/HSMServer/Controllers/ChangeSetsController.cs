using HSMServer.Attributes;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.ChangeSets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    /// <summary>
    /// The change-sets page of the web UI (#1570): the undo journal's list,
    /// before/after details and the rollback action with confirmation.
    /// </summary>
    // The page reads for every logged-in user (visibility per entry via the
    // caller's product/folder roles — the cookie twin of the token-side
    // filtering); ROLLBACK is admin-only, mirroring the alert-schedules
    // precedent (the Global boundary of schedule entries is admin-only on
    // the token side too).
    [Authorize]
    public sealed class ChangeSetsController : BaseController
    {
        private readonly IChangeSetAdministrationService _changeSets;
        private readonly ITreeValuesCache _cache;

        public ChangeSetsController(IChangeSetAdministrationService changeSets, IUserManager users,
            ITreeValuesCache cache)
            : base(users)
        {
            _changeSets = changeSets;
            _cache = cache;
        }


        public IActionResult Index() => View("Index");

        /// <summary>The table page: newest change sets visible to the caller.</summary>
        [HttpGet]
        public IActionResult GetChangeSetsTable(int limit = 100, int offset = 0)
        {
            var result = _changeSets.ListChangeSets(User, new ChangeSetFilter { Limit = limit, Offset = offset },
                new CookieUserAccess(CurrentUser, _cache));

            return result.Success
                ? PartialView("_ChangeSetsTable", result.Value)
                : PartialView("_ChangeSetsTable", new System.Collections.Generic.List<ChangeSetSummaryDto>());
        }

        /// <summary>The details view: per-entity before/after images.</summary>
        [HttpGet]
        public IActionResult Details(Guid id)
        {
            var result = _changeSets.GetChangeSet(id, User, new CookieUserAccess(CurrentUser, _cache));

            return result.Success ? PartialView("_ChangeSetDetails", result.Value) : NotFound();
        }

        /// <summary>Rollback with force; dry-run variant feeds the confirmation dialog.</summary>
        [HttpPost]
        [AuthorizeIsAdmin]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Rollback(Guid id, bool force = false, bool dryRun = false)
        {
            var result = await _changeSets.RollbackAsync(id, User, force, dryRun,
                new CookieUserAccess(CurrentUser, _cache));

            return result.Success ? Json(result.Value) : Json(new { error = result.Failure.Message });
        }
    }
}
