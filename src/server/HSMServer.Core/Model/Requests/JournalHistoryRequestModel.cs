using System;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;

namespace HSMServer.Core.Model.Requests;

public record JournalHistoryRequestModel(Guid Id, DateTime From = default, DateTime To = default, RecordType FromType = RecordType.Actions, RecordType ToType = RecordType.Changes, int Count = TreeValuesCache.MaxHistoryCount);