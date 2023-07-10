using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace HSMServer.Controllers.DataTables;

public class Test
{
    public DataTableParameters Parameters { get; set; }

    public Test()
    {
        
    }
}

public class Search
{
    public bool Regex { get; set; }
    public string Value { get; set; }

    public Search()
    {
        
    }
}

public class DataTableParameters
{
    public List<DataTableColumn> Columns { get; set; }
    public int Draw { get; set; }
    public int Length { get; set; }
    public List<DataTableOrder> Order { get; set; }
    
    public Search Search { get; set; } 
    public int Start { get; set; }

    public DataTableParameters()
    {
    }

    /// <summary>
    /// Retrieve DataTable parameters from WebMethod parameter, sanitized against parameter spoofing
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public static DataTableParameters Get(object input)
    {
        return Get(JObject.FromObject(input)["parameters"]);
    }

    /// <summary>
    /// Retrieve DataTable parameters from JSON, sanitized against parameter spoofing
    /// </summary>
    /// <param name="input">JToken object</param>
    /// <returns>parameters</returns>
    // public static DataTableParameters Get(JToken input)
    // {
    //     return new DataTableParameters
    //     {
    //         Columns = DataTableColumn.Get(input),
    //         Order = DataTableOrder.Get(input),
    //         Draw = (int)input["draw"],
    //         Start = (int)input["start"],
    //         Length = (int)input["length"],
    //         SearchValue =
    //             new string(
    //                 ((string)input["search"]["value"]).Where(
    //                     c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == '-').ToArray()),
    //         SearchRegex = (bool)input["search"]["regex"]
    //     };
    // }
}

public class DataTableColumn
{
    public int Data { get; set; }
    public string Name { get; set; }
    public bool Orderable { get; set; }
    public bool Searchable { get; set; }
    public Search Search { get; set; }

    public DataTableColumn()
    {
    }

    /// <summary>
    /// Retrieve the DataTables Columns dictionary from a JSON parameter list
    /// </summary>
    /// <param name="input">JToken object</param>
    /// <returns>Dictionary of Column elements</returns>
    // public static Dictionary<int, DataTableColumn> Get(JToken input)
    // {
    //     return (
    //             (JArray)input["columns"])
    //         .Select(col => new DataTableColumn
    //         {
    //             Data = (int)col["data"],
    //             Name =
    //                 new string(
    //                     ((string)col["name"]).Where(
    //                         c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == '-').ToArray()),
    //             Searchable = (bool)col["searchable"],
    //             Orderable = (bool)col["orderable"],
    //             Search.Value = 
    //                 new string(
    //                     ((string)col["search"]["value"]).Where(
    //                         c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == '-').ToArray()),
    //             SearchRegex = (bool)col["search"]["regex"]
    //         })
    //         .ToDictionary(c => c.Data);
    // }
}

public class DataTableOrder
{
    public int Column { get; set; }
    public string Dir { get; set; }

    public DataTableOrder()
    {
    }

    /// <summary>
    /// Retrieve the DataTables order dictionary from a JSON parameter list
    /// </summary>
    /// <param name="input">JToken object</param>
    /// <returns>Dictionary of Order elements</returns>
    public static Dictionary<int, DataTableOrder> Get(JToken input)
    {
        return (
                (JArray)input["order"])
            .Select(col => new DataTableOrder
            {
                Column = (int)col["column"],
                Dir =
                    ((string)col["dir"]).StartsWith("desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC"
            })
            .ToDictionary(c => c.Column);
    }
}

public class DataTableResultSet
{
    /// <summary>Array of records. Each element of the array is itself an array of columns</summary>
    public List<List<string>> Data = new();

    /// <summary>value of draw parameter sent by client</summary>
    public int Draw { get; set; }

    /// <summary>filtered record count</summary>
    public int RecordsFiltered { get; set; }

    /// <summary>total record count in resultset</summary>
    public int RecordsTotal { get; set; }
}

public class DataTableResultError : DataTableResultSet
{
    public string Error { get; set; }
}