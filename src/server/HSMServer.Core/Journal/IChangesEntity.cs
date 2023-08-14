using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Journal;

public interface IChangesEntity
{
    event Action<JournalRecordModel> ChangesHandler;
}