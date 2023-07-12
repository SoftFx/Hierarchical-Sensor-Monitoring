using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMServer.Controllers.DataTables;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Extensions;

namespace HSMServer.Model.History;

public class SelectedJournalViewModel
{
    private readonly object _lock = new ();


    private ICollection<JournalRecordModel> _journals;
    
    private Guid _id;
    private DataTableParameters _parameters;
    private JournalHistoryRequestModel _journalHistoryRequestModel;

    public int Length => _journals.Count;

    public async Task ConnectJournal(Guid id, IJournalService journalService)
    {
        if (_id == id)
            return;
        
        _id = id;
        journalService.NewJournalEvent -= AddNewJournals;
        journalService.NewJournalEvent += AddNewJournals;
        _journalHistoryRequestModel = new JournalHistoryRequestModel(_id, To: DateTime.MaxValue);

        _journals = await GetJournals(journalService);
    }

    public IEnumerable<JournalRecordModel> GetPage(DataTableParameters parameters)
    {
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

    private static IEnumerable<JournalRecordModel> Order(IOrderedEnumerable<JournalRecordModel> ordered, List<DataTableOrder> orders)
    {
        foreach (var order in orders)
        {
            var descending = order.Dir != "asc";
            ordered = order.Column switch
                {
                    0 => ordered.CreateOrderedEnumerable(x => x.Key.Time, null, descending),
                    1 => ordered.CreateOrderedEnumerable(x => x.Initiator, null, descending),
                    2 => ordered.CreateOrderedEnumerable(x => x.Key.Type, null, descending),
                    3 => ordered.CreateOrderedEnumerable(x => x.Value, null, descending),
                    _ => ordered
                };
        }

        return ordered;
    }

    private void AddNewJournals(JournalRecordModel record)
    {
        lock (_lock)
        {
            _journals.Add(record);
        }
    }

    private async Task<ICollection<JournalRecordModel>> GetJournals(IJournalService journalService)
    {
        return await journalService.GetJournalValuesPage(_journalHistoryRequestModel).Flatten();
    }
}