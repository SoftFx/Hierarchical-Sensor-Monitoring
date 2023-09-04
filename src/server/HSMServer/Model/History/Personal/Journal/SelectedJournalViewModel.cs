using HSMServer.Controllers.DataTables;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Extensions;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Model.History.Personal.Journal;

public sealed class SelectedJournalViewModel : ConcurrentDictionary<Guid, ConcurrentQueue<JournalRecordViewModel>>, IDisposable
{
    private const int MaxRecordsOnOneNode = 100;

    //private DataTableParameters _tableFilters;

    private IJournalService _journal;
    private BaseNodeViewModel _node;
    private int _totalSize;

    public int TotalSize => _totalSize;


    public Task ConnectJournal(BaseNodeViewModel baseNode, IJournalService journal)
    {
        if (_node?.Id == baseNode?.Id)
            return Task.CompletedTask;

        if (_journal is null)
        {
            _journal = journal;
            _journal.NewRecordEvent += SaveNewRecords;
        }

        if (_node is not null)
            _node.CheckJournalCount -= CheckJournalCount;
        
        _node = baseNode;
        _node.CheckJournalCount += CheckJournalCount;

        Interlocked.Exchange(ref _totalSize, 0);
        Clear();

        return Subscribe(_node);
    }

    private Task Subscribe(BaseNodeViewModel node)
    {
        var requests = new List<Task>()
        {
            Task.Run(() => LoadRecords(node.Id)),
        };

        if (node is FolderModel folder)
        {
            foreach (var (_, product) in folder.Products)
                requests.Add(Task.Run(() => Subscribe(product)));
        }
        else if (node is ProductNodeViewModel product)
        {
            foreach (var (_, subNode) in product.Nodes)
                requests.Add(Task.Run(() => Subscribe(subNode)));

            foreach (var (id, _) in product.Sensors)
                requests.Add(Task.Run(() => LoadRecords(id))); 
        }

        return Task.WhenAll(requests);
    }


    private async Task LoadRecords(Guid nodeId)
    {
        var request = new JournalHistoryRequestModel(nodeId)
        {
            Types = JournalHistoryRequestModel.AllTypes,
            From = DateTime.UtcNow.AddYears(-1),
            Count = -MaxRecordsOnOneNode,
        };

        var records = await _journal.GetPages(request).Flatten();

        Interlocked.Add(ref _totalSize, records.Count);
        TryAdd(nodeId, new ConcurrentQueue<JournalRecordViewModel>(records.Select(ToView)));
    }


    public (IEnumerable<JournalRecordViewModel> journals, int filteredSize) GetPage(DataTableParameters filter)
    {
        //if (_tableFilters == filter) //doesn't work because of DataTableParameters has referense on a list
        //    return _journals;
        //_tableFilters = filter;

        var records = GetFilteredList(filter.Search.Value);

        foreach (var order in filter.Order)
            if (Enum.TryParse<ColumnName>(filter.Columns[order.Column].Name, out var type))
            {
                var ascending = order.Dir == "asc";

                string FilterFunc(JournalRecordViewModel r) => r[type];
                DateTime FilterByDate(JournalRecordViewModel r) => r.Time;
                records = (type is ColumnName.Date ?ascending ? records.OrderBy(FilterByDate) : records.OrderByDescending(FilterByDate) : ascending ? records.OrderBy(FilterFunc) : records.OrderByDescending(FilterFunc)).ToList();
            }

        return (records.Skip(filter.Start).Take(filter.Length), records.Count);
    }

    private List<JournalRecordViewModel> GetFilteredList(string search)
    {
        bool Filter(JournalRecordViewModel record) => record.SearchValue.Contains(search, StringComparison.OrdinalIgnoreCase);
        bool EmptyFilter(JournalRecordViewModel _) => true;

        Func<JournalRecordViewModel, bool> filter = string.IsNullOrEmpty(search) ? EmptyFilter : Filter;

        return Values.SelectMany(x => x.Where(filter)).ToList();
    }

    private void SaveNewRecords(JournalRecordModel record)
    {
        if (TryGetValue(record.Key.Id, out var queue))
        {
            Interlocked.Increment(ref _totalSize);
            queue.Enqueue(ToView(record));

            while (queue.Count > MaxRecordsOnOneNode)
                if (queue.TryDequeue(out _))
                    Interlocked.Decrement(ref _totalSize);
                else
                    break;
        }
    }

    private JournalRecordViewModel ToView(JournalRecordModel record) => new(record);

    private bool CheckJournalCount() => TotalSize == 0;
    
    public void Dispose()
    {
        if (_journal is not null)
            _journal.NewRecordEvent -= SaveNewRecords;
    }
}