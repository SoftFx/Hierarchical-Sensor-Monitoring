using System.Collections.Generic;

namespace HSMServer.Controllers.DataTables;

public record TableParameters(DataTableParameters Parameters);

public record Search(bool Regex, string Value);

public record DataTableParameters(List<DataTableColumn> Columns, int Draw, int Length, List<DataTableOrder> Order, Search Search, int Start);

public record DataTableColumn(int Data, string Name, bool Orderable, bool Searchable, Search Search);

public record DataTableOrder(int Column, string Dir);

public class DataTableResultSet
{
    public List<List<string>> Data = new();

    public int Draw { get; set; }

    public int RecordsFiltered { get; set; }

    public int RecordsTotal { get; set; }
}

public class DataTableResultError : DataTableResultSet
{
    public string Error { get; set; }
}