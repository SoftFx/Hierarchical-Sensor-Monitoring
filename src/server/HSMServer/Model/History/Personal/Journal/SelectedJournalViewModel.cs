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

public sealed class SelectedJournalViewModel : IDisposable
{
    private const int MaxRecordsOnOneNode = 100;

    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<JournalRecordViewModel>> _subNodeRecords = new();

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

        Interlocked.Exchange(ref _totalSize, 0);

        _subNodeRecords.Clear();
        _node = baseNode;

        return Subscribe(_node);
    }

    private async Task Subscribe(BaseNodeViewModel node)
    {
        var requests = new List<Task>()
        {
            LoadRecords(node.Id),
        };

        if (node is FolderModel folder)
        {
            foreach (var (_, product) in folder.Products)
                await Subscribe(product);
        }
        else if (node is ProductNodeViewModel product)
        {
            foreach (var (_, subNode) in product.Nodes)
                await Subscribe(subNode);

            foreach (var (id, _) in product.Sensors)
                requests.Add(LoadRecords(id));
        }

        await Task.WhenAll(requests);
    }


    private async Task LoadRecords(Guid nodeId)
    {
        var request = new JournalHistoryRequestModel(nodeId)
        {
            From = DateTime.UtcNow.AddYears(-1),
            Count = -MaxRecordsOnOneNode,
        };

        var records = await _journal.GetPages(request).Flatten();

        Interlocked.Add(ref _totalSize, records.Count);

        _subNodeRecords.TryAdd(nodeId, new ConcurrentQueue<JournalRecordViewModel>(records.Select(ToView)));
    }


    public IEnumerable<JournalRecordViewModel> GetPage(DataTableParameters filter)
    {
        //if (_tableFilters == filter) //doesn't work because of DataTableParameters has referense on a list
        //    return _journals;
        //_tableFilters = filter;

        var records = GetFilteredList(filter.Search.Value);

        foreach (var order in filter.Order)
            if (Enum.TryParse<ColumnName>(filter.Columns[order.Column].Name, out var type))
            {
                var ascending = order.Dir == "asc";

                Func<JournalRecordViewModel, string> filterFunc = type switch
                {
                    ColumnName.Date => r => r.TimeAsString,
                    ColumnName.Initiator => x => x.Initiator,
                    ColumnName.Type => x => x.Type.ToString(),
                    ColumnName.Record => x => x.Value,
                    ColumnName.Path => x => x.Path,
                    _ => throw new NotImplementedException(),
                };

                records = (ascending ? records.OrderBy(filterFunc) : records.OrderByDescending(filterFunc)).ToList();
            }

        return records.Skip(filter.Start).Take(filter.Length);
    }

    private List<JournalRecordViewModel> GetFilteredList(string search)
    {
        bool Filter(JournalRecordViewModel record) => record.Value.Contains(search, StringComparison.OrdinalIgnoreCase);
        bool EmptyFilter(JournalRecordViewModel _) => true;

        Func<JournalRecordViewModel, bool> filter = string.IsNullOrEmpty(search) ? EmptyFilter : Filter;

        return _subNodeRecords.Values.SelectMany(x => x.Where(filter)).ToList();
    }

    private void SaveNewRecords(JournalRecordModel record)
    {
        if (_subNodeRecords.TryGetValue(record.Key.Id, out var queue))
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


    public void Dispose()
    {
        if (_journal is not null)
            _journal.NewRecordEvent -= SaveNewRecords;
    }
}