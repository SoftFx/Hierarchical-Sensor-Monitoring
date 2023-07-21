using HSMServer.Authentication;
using HSMServer.Controllers.DataTables;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;

namespace HSMServer.Controllers;


public class JournalController : BaseController
{
    public JournalController(IUserManager userManager) : base(userManager) { }


    [HttpPost]
    public JsonResult GetPage([FromBody] DataTableParameters parameters)
    {
        var draw = parameters.Draw;
        var rows = new List<List<string>>(parameters.Length);

        foreach (var recordFromDb in StoredUser.Journal.GetPage(parameters))
        {
            var cells = new List<string>(parameters.Columns.Count);

            foreach (var column in parameters.Columns)
                if (Enum.TryParse<ColumnName>(column.Name, out var name))
                {
                    var value = recordFromDb[name];

                    if (name is ColumnName.Record)
                        value = value.Replace(Environment.NewLine, "<br>");

                    cells.Add(value);
                }

            rows.Add(cells);
        }

        return Json(new DataTableResultSet(draw, StoredUser.Journal.TotalSize, StoredUser.Journal.TotalSize, rows));
    }
}