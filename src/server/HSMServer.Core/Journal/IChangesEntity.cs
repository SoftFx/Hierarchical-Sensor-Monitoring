using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Journal;

public interface IChangesEntity
{
    string ChangesEnviromentName { get; }


    event Action<JournalRecordModel> ChangesHandler;
}