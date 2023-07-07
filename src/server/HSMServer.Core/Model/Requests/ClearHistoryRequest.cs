using System;

namespace HSMServer.Core.Model.Requests;

public record ClearHistoryRequest(Guid Id, string Caller = "Not specified", DateTime To = default);