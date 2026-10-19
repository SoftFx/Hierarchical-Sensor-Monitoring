using HSMServer.Attributes;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.ChangeSets;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Security.Claims;
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

        public ChangeSetsController(IChangeSetAdministrationService changeSets, IUserManager users)
            : base(users) =>
            _changeSets = changeSets;


        public IActionResult Index() => View("Index");

        /// <summary>The table page: newest change sets visible to the caller.</summary>
        [HttpGet]
        public IActionResult GetChangeSetsTable(int limit = 100, int offset = 0)
        {
            var result = _changeSets.ListChangeSets(User, new ChangeSetFilter { Limit = limit, Offset = offset },
                new CookieUserAccess(CurrentUser));

            return result.Success
                ? PartialView("_ChangeSetsTable", result.Value)
                : PartialView("_ChangeSetsTable", new System.Collections.Generic.List<ChangeSetSummaryDto>());
        }

        /// <summary>The details view: per-entity before/after images.</summary>
        [HttpGet]
        public IActionResult Details(Guid id)
        {
            var result = _changeSets.GetChangeSet(id, User, new CookieUserAccess(CurrentUser));

            return result.Success ? PartialView("_ChangeSetDetails", result.Value) : NotFound();
        }

        /// <summary>Rollback with force; dry-run variant feeds the confirmation dialog.</summary>
        [HttpPost]
        [AuthorizeIsAdmin]
        public async Task<IActionResult> Rollback(Guid id, bool force = false, bool dryRun = false)
        {
            var result = await _changeSets.RollbackAsync(id, User, force, dryRun,
                new CookieUserAccess(CurrentUser));

            return result.Success ? Json(result.Value) : Json(new { error = result.Failure.Message });
        }


        // The cookie-`User` twin of the token evaluator's per-entry checks:
        // the evaluator resolves HsmApiToken principals only, so the UI maps
        // its role predicates onto the same boundaries. Admin sees and rolls
        // back everything; product/folder roles gate their own entities;
        // schedule entries (Global) stay admin-only.
        private sealed class CookieUserAccess : IChangeSetAccess
        {
            private readonly User _user;

            public CookieUserAccess(User user) => _user = user;

            public bool CanRead(ChangeSetEntry entry) =>
                _user is null ? false : EntryResourceOf(entry) switch
                {
                    { Kind: ApiTokenResourceKind.Global } => _user.IsAdmin,
                    { Kind: ApiTokenResourceKind.Folder } resource => _user.IsAdmin || _user.IsFolderManager(resource.Id),
                    { Kind: ApiTokenResourceKind.Product } resource => _user.IsProductAvailable(resource.Id),
                    { Kind: ApiTokenResourceKind.Sensor } resource => _user.IsProductAvailable(entry.ParentId ?? resource.Id),
                    _ => false,
                };

            public bool CanWrite(ChangeSetEntry entry) =>
                _user is null ? false : EntryResourceOf(entry) switch
                {
                    { Kind: ApiTokenResourceKind.Global } => _user.IsAdmin,
                    { Kind: ApiTokenResourceKind.Folder } resource => _user.IsFolderManager(resource.Id),
                    { Kind: ApiTokenResourceKind.Product } resource => _user.IsManager(resource.Id),
                    { Kind: ApiTokenResourceKind.Sensor } resource => _user.IsManager(entry.ParentId ?? resource.Id),
                    _ => false,
                };

            private static (ApiTokenResourceKind Kind, Guid Id)? EntryResourceOf(ChangeSetEntry entry) =>
                entry.Kind switch
                {
                    ChangeSetEntryKind.SensorDataPolicies or ChangeSetEntryKind.SensorTtlPolicies =>
                        (ApiTokenResourceKind.Sensor, entry.EntityId),
                    ChangeSetEntryKind.ProductTtlPolicies => (ApiTokenResourceKind.Product, entry.EntityId),
                    ChangeSetEntryKind.AlertTemplate when entry.ParentId is { } folder =>
                        (ApiTokenResourceKind.Folder, folder),
                    ChangeSetEntryKind.AlertSchedule => (ApiTokenResourceKind.Global, Guid.Empty),
                    _ => null,
                };
        }
    }
}
