using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMServer.Controllers.DataTables;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.History;

public class SelectedJournalViewModel
{
    private readonly object _lock = new ();

    private ConcurrentDictionary<Guid, int> IdsToSubscribe => new(_baseNode is ProductNodeViewModel node
        ? node.Sensors.ToDictionary(x => x.Key, x => 1)
        : Enumerable.Empty<KeyValuePair<Guid, int>>());
    
    private List<JournalRecordModel> _journals;
    
    private BaseNodeViewModel _baseNode;

    private DataTableParameters _parameters;
    private JournalHistoryRequestModel _journalHistoryRequestModel;

    public int Length => _journals.Count;

    public async Task ConnectJournal(BaseNodeViewModel baseNode, IJournalService journalService)
    {
        if (_baseNode?.Id == baseNode.Id)
            return;
        
        _baseNode = baseNode;

        journalService.NewJournalEvent -= AddNewJournals;
        journalService.NewJournalEvent += AddNewJournals;
        _journalHistoryRequestModel = new JournalHistoryRequestModel(_baseNode.Id, To: DateTime.MaxValue);

        _journals = await GetJournals(journalService);
        if (_baseNode is ProductNodeViewModel node)
            foreach (var id in node.Sensors.Keys)
                _journals.AddRange(await journalService.GetPages(_journalHistoryRequestModel with { Id = id }).Flatten());
    }

    public IEnumerable<JournalRecordModel> GetPage(DataTableParameters parameters)
    {
        if (_parameters == parameters)
            return _journals;
        
        _parameters = parameters;

        var searched = Search(parameters.Search.Value);

        return Order(searched.OrderBy(x => x, new JournalEmptyComparer()), _parameters.Order).Skip(_parameters.Start).Take(_parameters.Length);
    }

    private IEnumerable<JournalRecordModel> Search(string search)
    {
        return !string.IsNullOrEmpty(search)
            ? _journals.Where(x => x.Value.Contains(search, StringComparison.OrdinalIgnoreCase))
            : _journals;
    }

    private IEnumerable<JournalRecordModel> Order(IOrderedEnumerable<JournalRecordModel> ordered, List<DataTableOrder> orders)
    {
        for (int i = 0; i < orders.Count; i++)
        {
            var descending = orders[i].Dir != "asc";
            ordered = _parameters.Columns[orders[i].Column].GetColumnName() switch
            {
                ColumnName.Date => ordered.CreateOrderedEnumerable(x => x.Key.Time, null, descending),
                ColumnName.Initiator => ordered.CreateOrderedEnumerable(x => x.Initiator, null, descending),
                ColumnName.Type => ordered.CreateOrderedEnumerable(x => x.Key.Type, null, descending),
                ColumnName.Record => ordered.CreateOrderedEnumerable(x => x.Value, null, descending),
                ColumnName.Name => ordered.CreateOrderedEnumerable(x => x.Name, null, descending),
                _ => ordered
            };
        }

        return ordered;
    }

    private void AddNewJournals(JournalRecordModel record)
    {
        lock (_lock)
        {
            if (IdsToSubscribe.TryGetValue(record.Key.Id, out _) || _baseNode.Id == record.Key.Id)
                _journals.Add(record);
        }
    }

    private async Task<List<JournalRecordModel>> GetJournals(IJournalService journalService)
    {
        return await journalService.GetPages(_journalHistoryRequestModel).Flatten();
    }
}