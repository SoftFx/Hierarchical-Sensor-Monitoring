using System;

namespace HSMServer.DTOs.Sources;

public sealed record DeleteSourcesDto (Guid[] Ids);