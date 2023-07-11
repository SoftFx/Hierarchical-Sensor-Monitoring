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
        journalService.NewJournalEvent += AddNewJournals;
        _journalHistoryRequestModel = new JournalHistoryRequestModel(_id, To: DateTime.MaxValue);

        _journals = await GetJournals(journalService);
    }

    public IEnumerable<JournalRecordModel> GetPage(DataTableParameters parameters)
    {
        _parameters = parameters;

        var searched = Search(parameters.Search.Value);

        return Order(searched, _parameters.Order[0]).Skip(_parameters.Start).Take(_parameters.Length);
    }

    private IEnumerable<JournalRecordModel> Search(string search)
    {
        return !string.IsNullOrEmpty(search)
            ? _journals.Where(x => x.Value.Contains(search, StringComparison.OrdinalIgnoreCase))
            : _journals;
    }

    private IEnumerable<JournalRecordModel> Order(IEnumerable<JournalRecordModel> items, DataTableOrder order)
    {
        if (order.Dir == "asc")
            return order.Column switch
            {
                0 => items.OrderBy(x => x.Key.Time),
                1 => items.OrderBy(x => x.Key.Type),
                2 => items.OrderBy(x => x.Value),
                3 => items.OrderBy(x => x.Initiator),
                _ => items
            };

        return order.Column switch
            {
                0 => items.OrderByDescending(x => x.Key.Time),
                1 => items.OrderByDescending(x => x.Key.Type),
                2 => items.OrderByDescending(x => x.Value),
                3 => items.OrderByDescending(x => x.Initiator),
                _ => items
            };
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