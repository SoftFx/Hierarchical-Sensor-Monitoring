using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Authentication;
using HSMServer.Controllers.DataTables;
using HSMServer.Core.Journal;
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
    public JsonResult GetPage([FromBody] TableParameters parameters)
    {
        var resultSet = new DataTableResultSet
        {
            Draw = parameters.Parameters.Draw
        };
        
        foreach (var recordFromDb in StoredUser.Journal.GetPage(parameters.Parameters).Select(x => new JournalViewModel(x)))
        {
            var data = new List<string>
            {
                recordFromDb.TimeAsString,
                recordFromDb.Type.ToString(),
                recordFromDb.Value.Replace(Environment.NewLine, "<br>"),
                recordFromDb.Initiator,
            };
            resultSet.Data.Add(data);
        }

        resultSet.RecordsTotal = StoredUser.Journal.Length;
        resultSet.RecordsFiltered = StoredUser.Journal.Length;

        return Json( new
        {
            data = resultSet.Data.ToArray(),
            resultSet.RecordsFiltered,
            resultSet.RecordsTotal,
            resultSet.Draw
        });
    }
}