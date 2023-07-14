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
    public JsonResult GetPage([FromBody] DataTableParameters parameters)
    {
        var draw = parameters.Draw;
        var rows = new List<List<string>>(1 << 5);
        
        foreach (var recordFromDb in StoredUser.Journal.GetPage(parameters).Select(x => new JournalViewModel(x)))
        {
            var data = new List<string>(1 << 4);

            foreach (var name in parameters.Columns.Select(x => x.GetColumnName()))
            {
                switch (name)
                {
                    case ColumnName.Date:
                        data.Add(recordFromDb.TimeAsString);
                        break;
                    case ColumnName.Name:
                        data.Add(recordFromDb.Name);
                        break;
                    case ColumnName.Type:
                        data.Add(recordFromDb.Type.ToString());
                        break;
                    case ColumnName.Record:
                        data.Add(recordFromDb.Value.Replace(Environment.NewLine, "<br>"));
                        break;
                    case ColumnName.Initiator:
                        data.Add(recordFromDb.Initiator);
                        break;
                }
            }

            rows.Add(data);
        }

        return Json(new DataTableResultSet(draw, StoredUser.Journal.Length, StoredUser.Journal.Length, rows));
    }
}