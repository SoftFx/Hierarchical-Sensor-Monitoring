using HSMServer.Authentication;
using HSMServer.Controllers.DataTables;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Controllers;


public class JournalController : BaseController
{
    public JournalController(IUserManager userManager) : base(userManager) { }


    [HttpPost]
    public JsonResult GetPage([FromBody] DataTableParameters parameters)
    {
        var draw = parameters.Draw;
        var rows = new List<List<string>>(1 << 5);

        foreach (var recordFromDb in StoredUser.Journal.GetPage(parameters))
        {
            var data = new List<string>(1 << 4);

            foreach (var name in parameters.Columns.Select(x => x.GetColumnName()))
            {
                switch (name)
                {
                    case ColumnName.Date:
                        data.Add(recordFromDb.TimeAsString);
                        break;
                    case ColumnName.Path:
                        data.Add(recordFromDb.Path);
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

        return Json(new DataTableResultSet(draw, StoredUser.Journal.TotalSize, StoredUser.Journal.TotalSize, rows));
    }
}