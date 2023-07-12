using System.Collections.Generic;
using HSMServer.Core.Model;

namespace HSMServer.Core.Journal;

public class JournalEmptyComparer : IComparer<JournalRecordModel>
{
    public int Compare(JournalRecordModel x, JournalRecordModel y) => 0;
}