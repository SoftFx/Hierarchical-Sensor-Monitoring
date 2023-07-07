using System;
using HSMServer.Core.Model;

namespace HSMServer.Core.Journal;

public interface IJournal
{
    event Action<JournalRecordModel> CreateJournal;
}