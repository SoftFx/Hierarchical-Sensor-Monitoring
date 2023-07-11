using System.Collections.Generic;

namespace HSMServer.Controllers.DataTables;

public class TableParameters
{
    public DataTableParameters Parameters { get; set; }


    public TableParameters() { }
}

public class Search
{
    public bool Regex { get; set; }

    public string Value { get; set; }

    public Search() { }
}

public class DataTableParameters
{
    public List<DataTableColumn> Columns { get; set; }

    public int Draw { get; set; }

    public int Length { get; set; }

    public List<DataTableOrder> Order { get; set; }
    
    public Search Search { get; set; } 

    public int Start { get; set; }


    public DataTableParameters() { }
}

public class DataTableColumn
{
    public int Data { get; set; }

    public string Name { get; set; }

    public bool Orderable { get; set; }

    public bool Searchable { get; set; }

    public Search Search { get; set; }


    public DataTableColumn() { }
}

public class DataTableOrder
{
    public int Column { get; set; }

    public string Dir { get; set; }


    public DataTableOrder() { }
}

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