using HSMServer.Attributes;
using HSMServer.Authentication;
using HSMServer.Controllers.DataTables;
using HSMServer.Core.Cache;
using HSMServer.Core.Journal;
using HSMServer.Model.ManagementApi.Journal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Controllers;


/// <summary>
/// The unified Journal surface (#1588): the per-node tab's DataTables feed
/// (GetPage) and the global Journal page — every configuration change from
/// every source with author/time/kind/product filters, per-record details
/// and the admin-only rollback by record.
/// </summary>
// The global page reads for every logged-in user (visibility per record via
// the caller's product/folder roles — the cookie twin of the token-side
// filtering); ROLLBACK is admin-only, mirroring the alert-schedules
// precedent.
[Authorize]
public class JournalController : BaseController
{
    private readonly JournalAdministrationService _journal;
    private readonly ITreeValuesCache _cache;

    public JournalController(IUserManager userManager, JournalAdministrationService journal,
        ITreeValuesCache cache) : base(userManager)
    {
        _journal = journal;
        _cache = cache;
    }


    [HttpPost]
    public JsonResult GetPage([FromBody] DataTableParameters parameters)
    {
        var draw = parameters.Draw;
        var rows = new List<List<string>>(parameters.Length);
        var (recordsFromDb, filteredRecords) = StoredUser.Journal.GetPage(parameters);
        foreach (var recordFromDb in recordsFromDb)
        {
            var cells = new List<string>(parameters.Columns.Count);

            foreach (var column in parameters.Columns)
                if (Enum.TryParse<ColumnName>(column.Name, out var name))
                {
                    var value = recordFromDb[name];

                    if (name is ColumnName.Record)
                    {
                        value = value?.Replace(Environment.NewLine, "<br>");

                        // The per-node tab's rollback action (#1588): the
                        // Record cell carries the button for rollbackable
                        // records; journalPage.js (main bundle) wires the
                        // click on every page it appears.
                        if (recordFromDb.Rollbackable && CurrentUser.IsAdmin)
                            value = $"{value}<button type=\"button\" class=\"btn btn-link p-0 text-danger journal-record-rollback\" data-record-id=\"{recordFromDb.Id}\">Rollback</button>";
                    }

                    cells.Add(value);
                }

            rows.Add(cells);
        }

        return Json(new DataTableResultSet(draw, filteredRecords, StoredUser.Journal.TotalSize, rows));
    }


    // === The global Journal page (#1588) ===

    public IActionResult Index()
    {
        var access = new CookieJournalRecordAccess(CurrentUser, _cache);

        ViewBag.Products = _cache.GetProducts()
            .Where(p => access.CanRead(Authentication.ApiTokenResource.Product(p.Id)))
            .Select(p => (p.Id, p.DisplayName))
            .OrderBy(p => p.DisplayName)
            .ToList();

        return View("Index");
    }

    /// <summary>The table page: newest configuration records visible to the caller.</summary>
    [HttpGet]
    public IActionResult GetJournalTable(string author = null, string kind = null, Guid? productId = null,
        DateTime? fromUtc = null, DateTime? toUtc = null, int limit = 100, int offset = 0)
    {
        var result = _journal.ListRecords(User, new JournalFilter
        {
            Author = author,
            Kind = kind,
            ProductId = productId,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Limit = limit,
            Offset = offset,
        }, new CookieJournalRecordAccess(CurrentUser, _cache));

        return PartialView("_JournalTable",
            result.Success ? result.Value : new List<JournalRecordDto>());
    }

    /// <summary>The details view: the record plus its undo group's before/after images.</summary>
    [HttpGet]
    public IActionResult Details(string id)
    {
        var result = _journal.GetRecord(id, User, new CookieJournalRecordAccess(CurrentUser, _cache));

        return result.Success ? PartialView("_JournalDetails", result.Value) : NotFound();
    }

    /// <summary>Rollback by journal record (the record's undo group); dry-run feeds the confirmation dialog.</summary>
    [HttpPost]
    [AuthorizeIsAdmin]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rollback(string id, bool force = false, bool dryRun = false)
    {
        var result = await _journal.RollbackAsync(id, User, force, dryRun,
            new CookieJournalRecordAccess(CurrentUser, _cache));

        return result.Success ? Json(result.Value) : Json(new { error = result.Failure.Message });
    }
}
