using System.Collections.Generic;

namespace HSMServer.Model.TreeViewModel
{
    public sealed record ChildrenInfo(string Title, int Total, List<(SensorStatus Status, int Count)> Statuses);
}
