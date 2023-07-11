using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Controllers.DataTables;
using HSMServer.Core.Cache;
using HSMServer.Core.Journal;
using HSMServer.Extensions;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class JournalController : BaseController
{
    private readonly IJournalService _journalService;
    
    public JournalController(IUserManager userManager, IJournalService journalService) : base(userManager)
    {
        _journalService = journalService;
    }

    [HttpPost]
    public async Task<JsonResult> GetPage([FromQuery] string id, [FromBody] TableParameters parameters)
    {
        var req = parameters.Parameters;
        var resultSet = new DataTableResultSet
        {
            Draw = req.Draw
        };

        var journals = await _journalService
            .GetJournalValuesPage(new(Guid.Parse(id), DateTime.MinValue, DateTime.MaxValue, RecordType.Changes, TreeValuesCache.MaxHistoryCount)).Flatten();
        
        var changesJournals = (await _journalService
            .GetJournalValuesPage(new(Guid.Parse(id), DateTime.MinValue, DateTime.MaxValue, RecordType.Actions, TreeValuesCache.MaxHistoryCount))
            .Flatten()).ToList();
        
        journals.AddRange(changesJournals);
        var searched = journals.Where(x => x.Value.Contains(req.Search.Value, StringComparison.OrdinalIgnoreCase));
        
        if (req.Order is not null && req.Order.Count > 0)
        {
            if (req.Order[0].Dir == "asc")
            {
                searched = req.Order[0].Column switch
                {
                    0 => searched.OrderBy(x => x.Key.Time),
                    1 => searched.OrderBy(x => x.Key.Type),
                    2 => searched.OrderBy(x => x.Value),
                    3 => searched.OrderBy(x => x.Initiator),
                    _ => searched
                };
            }
            else
            {
                searched = req.Order[0].Column switch
                {
                    0 => searched.OrderByDescending(x => x.Key.Time),
                    1 => searched.OrderByDescending(x => x.Key.Type),
                    2 => searched.OrderByDescending(x => x.Value),
                    3 => searched.OrderByDescending(x => x.Initiator),
                    _ => searched
                };
            }
        }

        foreach (var recordFromDb in searched.Skip(req.Start).Take(req.Length).Select(x => new JournalViewModel(x))) {
            var data = new List<string>
            {
                recordFromDb.TimeAsString,
                recordFromDb.Type.ToString(),
                recordFromDb.Value,
                recordFromDb.Initiator,
            };
            resultSet.Data.Add(data);
        }

        resultSet.RecordsTotal = journals.Count;
        resultSet.RecordsFiltered = journals.Count;

        return Json( new
        {
            data = resultSet.Data.ToArray(),
            resultSet.RecordsFiltered,
            resultSet.RecordsTotal,
            resultSet.Draw
        });
    }
}