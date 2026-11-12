using HSMServer.Core.Journal;
using HSMServer.Model.ManagementApi.Journal;
using Xunit;

namespace HSMServer.Core.Tests.Journal
{
    // #1638: the label rendering the Journal table shows for each operation
    // — one visible badge per operation tag (Create / Update / Delete /
    // Rollback) plus the rolled-back marker, shared by the global table
    // (_JournalTable.cshtml) and the per-node tab's Record cell.
    public class JournalOperationLabelsTests
    {
        [Theory]
        [InlineData(JournalOperations.Create, "Create")]
        [InlineData(JournalOperations.Update, "Update")]
        [InlineData(JournalOperations.Delete, "Delete")]
        [InlineData(JournalOperations.Rollback, "Rollback")]
        public void EveryOperationTag_RendersItsLabel(string operation, string label)
        {
            Assert.Equal(label, JournalOperationLabels.Label(operation));
        }

        [Fact]
        public void UnknownTag_RendersNoLabel()
        {
            Assert.Null(JournalOperationLabels.Label(null));
            Assert.Null(JournalOperationLabels.Label("nonsense"));
        }

        [Fact]
        public void Badge_CarriesLabelAndColor_UnknownTagIsEmpty()
        {
            var badge = JournalOperationLabels.Badge(JournalOperations.Delete);

            Assert.Contains(">Delete<", badge);
            Assert.Contains(JournalOperationLabels.CssClass(JournalOperations.Delete), badge);

            Assert.Equal(string.Empty, JournalOperationLabels.Badge(null));
            Assert.Equal(string.Empty, JournalOperationLabels.Badge("nonsense"));
        }

        [Fact]
        public void RolledBackBadge_MarksTheUndoneWrite()
        {
            Assert.Contains("rolled back", JournalOperationLabels.RolledBackBadge());
        }

        [Fact]
        public void Labels_CoverEveryOperationConstant()
        {
            // A new operation constant without a label would silently render
            // an empty column — pin the closed set.
            Assert.NotNull(JournalOperationLabels.Label(JournalOperations.Create));
            Assert.NotNull(JournalOperationLabels.Label(JournalOperations.Update));
            Assert.NotNull(JournalOperationLabels.Label(JournalOperations.Delete));
            Assert.NotNull(JournalOperationLabels.Label(JournalOperations.Rollback));
        }
    }
}
