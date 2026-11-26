using System;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Controllers.DataTables;
using HSMServer.Model.History.Personal.Journal;
using Xunit;

namespace HSMServer.Core.Tests.Journal
{
    // The Record cell of the per-node Journal tab is HTML (it carries <strong> and the operation badge)
    // and the table inserts it as markup. What the record holds is text typed by users and sent by
    // collectors (a description, a path, a name): it is encoded where the cell is built.
    public class JournalRecordEncodingTests
    {
        private const string Markup = "<img src=x onerror=alert(1)>";
        private const string Encoded = "&lt;img src=x onerror=alert(1)&gt;";


        [Theory]
        [InlineData("Description", Markup, "plain")]   // modified
        [InlineData("Description", "", Markup)]        // added
        [InlineData("Description", Markup, "")]        // removed
        [InlineData(Markup, "old", "new")]             // the property name itself
        public void ChangeRecord_EncodesTheRecordedText_AndKeepsItsOwnMarkup(string property, string oldValue, string newValue)
        {
            var view = new JournalRecordViewModel(Record(new JournalRecordEntity
            {
                Initiator = "System",
                PropertyName = property,
                OldValue = oldValue,
                NewValue = newValue,
            }));

            Assert.Contains(Encoded, view[ColumnName.Record]);
            Assert.DoesNotContain("<img", view[ColumnName.Record]);
            Assert.Contains("<strong>", view[ColumnName.Record]);
        }

        [Fact]
        public void FreeTextRecord_IsEncoded()
        {
            var view = new JournalRecordViewModel(Record(new JournalRecordEntity { Initiator = "System", Enviroment = Markup }));

            Assert.Contains(Encoded, view[ColumnName.Record]);
            Assert.DoesNotContain("<img", view[ColumnName.Record]);
        }

        [Fact]
        public void Search_StillMatchesTheTextAsTyped()
        {
            var view = new JournalRecordViewModel(Record(new JournalRecordEntity
            {
                Initiator = "System",
                PropertyName = "Description",
                OldValue = "a < b",
                NewValue = "a > b",
            }));

            Assert.Contains("a < b", view.SearchValue);
            Assert.Contains("a &lt; b", view[ColumnName.Record]);
        }


        private static JournalRecordModel Record(JournalRecordEntity entity) =>
            new(new JournalKey(Guid.NewGuid(), new DateTime(2024, 1, 1).Ticks, RecordType.Changes).GetBytes(), entity);
    }
}
