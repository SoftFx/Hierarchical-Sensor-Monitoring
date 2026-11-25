using HSMServer.Core.Model;
using System;

namespace HSMServer.Model.Folders
{
    /// <summary>
    /// One period a product inherits from its folder whose stored copy was re-derived
    /// at start (#1864): what it was and what it is now.
    /// </summary>
    internal sealed record FolderPeriodRepair(Guid ProductId, string ProductName, string FolderName, string Setting, TimeIntervalModel Old, TimeIntervalModel New);
}
