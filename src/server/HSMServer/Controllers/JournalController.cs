using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Controllers.DataTables;
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
    public async Task<JsonResult> GetPage([FromQuery] string id, [FromBody] Test parameters)
    {
        var req = parameters.Parameters;
        var resultSet = new DataTableResultSet
        {
            Draw = req.Draw,
            RecordsTotal = 5, 
            RecordsFiltered = 5
        };

        var items = (await _journalService
            .GetJournalValuesPage(new(Guid.Parse(id), DateTime.MinValue, DateTime.MaxValue, RecordType.Actions, -100))
            .Flatten()).Select(x => new JournalViewModel(x)).ToList();
        
        foreach (var recordFromDb in items) { /* this is pseudocode */
            var data = new List<string>
            {
                recordFromDb.TimeAsString,
                recordFromDb.Type.ToString(),
                recordFromDb.Value
            };
            resultSet.Data.Add(data);
        }

        return Json( new
        {
            data = resultSet.Data.ToArray(),
            resultSet.RecordsFiltered,
            resultSet.RecordsTotal,
            resultSet.Draw
        });
    }
}