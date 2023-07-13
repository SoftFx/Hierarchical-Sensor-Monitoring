using System;
using HSMServer.Core.Model;

namespace HSMServer.Core.Journal;

public interface IChangesEntity
{
    event Action<JournalRecordModel> ChangesHandler;
}