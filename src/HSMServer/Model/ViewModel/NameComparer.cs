using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    public class NameNodeComparer : Comparer<NodeViewModel>
    {
        public override int Compare(NodeViewModel x, NodeViewModel y)
        {
            return x.Name.CompareTo(y.Name);
        }
    }

    public class NameSensorComparer : Comparer<SensorViewModel>
    {
        public override int Compare(SensorViewModel x, SensorViewModel y)
        {
            return x.Name.CompareTo(y.Name);
        }
    }
}
