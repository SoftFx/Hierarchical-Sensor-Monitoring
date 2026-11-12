using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Model.History.Personal.Journal;
using HSMServer.Model.TreeViewModel;
using Xunit;

namespace HSMServer.Core.Tests.Journal
{
    // The per-node Journal tab keeps at most 100 records per node and evicts
    // from the head of its queue when a live record arrives, so the loaded
    // records must sit oldest first: a new record evicts the oldest one.
    public class SelectedJournalViewModelTests
    {
        private const int MaxRecordsOnOneNode = 100;

        private static readonly DateTime Start = new(2024, 1, 1);


        [Fact]
        public async Task LiveRecord_AtCap_EvictsOldestLoadedRecord()
        {
            var product = BuildProduct();
            var journal = new FakeJournalService(product.Id, MaxRecordsOnOneNode);

            using var view = new SelectedJournalViewModel();
            await view.ConnectJournal(product, journal);

            journal.Raise(Record(product.Id, MaxRecordsOnOneNode));

            var times = view[product.Id].Select(x => x.Time).ToList();

            Assert.Equal(MaxRecordsOnOneNode, times.Count);
            Assert.DoesNotContain(Start.AddMinutes(0), times);
            Assert.Contains(Start.AddMinutes(1), times);
            Assert.Contains(Start.AddMinutes(MaxRecordsOnOneNode - 1), times);
            Assert.Contains(Start.AddMinutes(MaxRecordsOnOneNode), times);
        }


        private static ProductNodeViewModel BuildProduct()
        {
            var model = new ProductModel(EntitiesFactory.BuildProductEntity(name: "journal") with { Id = Guid.NewGuid().ToString() });
            var product = new ProductNodeViewModel(model, parent: null, folder: null);

            product.Update(model);

            return product;
        }

        private static JournalRecordModel Record(Guid id, int minute) =>
            new(new JournalKey(id, Start.AddMinutes(minute).Ticks, RecordType.Changes).GetBytes(),
                new JournalRecordEntity { Initiator = "System", OldValue = $"m{minute}" });


        private sealed class FakeJournalService(Guid id, int stored) : IJournalService
        {
            public event Action<JournalRecordModel> NewRecordEvent;

            public void Raise(JournalRecordModel record) => NewRecordEvent?.Invoke(record);

            // A negative count is answered newest first, as the storage page read does.
            public async IAsyncEnumerable<List<JournalRecordModel>> GetPages(JournalHistoryRequestModel request)
            {
                Assert.True(request.Count < 0);

                await Task.Yield();

                yield return Enumerable.Range(0, stored).Reverse().Take(-request.Count).Select(m => Record(id, m)).ToList();
            }

            public void AddRecord(JournalRecordModel record) => throw new NotSupportedException();

            public void RemoveRecords(Guid id, Guid parentId = default) => throw new NotSupportedException();

            public List<JournalRecordModel> GetRecords(DateTime from, DateTime to) => throw new NotSupportedException();

            public JournalRecordModel GetRecord(JournalKey key) => throw new NotSupportedException();
        }
    }
}
