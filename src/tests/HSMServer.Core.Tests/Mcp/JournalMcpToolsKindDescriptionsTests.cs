using HSMServer.Core.Journal;
using HSMServer.Mcp;
using HSMServer.Model.ManagementApi.Journal;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace HSMServer.Core.Tests.Mcp
{
    // #1828: the journal tool describes the entity kinds by hand — the text is not rendered
    // from JournalKinds — so a kind added to the list and not to the description could be
    // filtered on REST while an agent reading the tool schema never learns it exists. The
    // description of the list tool and of its kind filter must name every kind.
    public class JournalMcpToolsKindDescriptionsTests
    {
        [Fact]
        public void ListJournalRecords_ToolAndKindFilterDescriptions_NameEveryKind()
        {
            var method = typeof(JournalMcpTools).GetMethod(nameof(JournalMcpTools.ListJournalRecordsAsync));
            var kindFilter = method.GetParameters().Single(p => p.Name == "kind");

            var descriptions = new[]
            {
                method.GetCustomAttribute<DescriptionAttribute>().Description,
                kindFilter.GetCustomAttribute<DescriptionAttribute>().Description,
            };

            // "other" is not a kind of the list — it is how legacy rows filter — but the
            // description names it as a filter value.
            foreach (var kind in JournalKinds.All.Append(JournalAdministrationService.OtherKind))
                Assert.All(descriptions, text =>
                    Assert.True(Regex.IsMatch(text, $@"\b{Regex.Escape(kind)}\b"), $"'{kind}' is missing from: {text}"));
        }
    }
}
