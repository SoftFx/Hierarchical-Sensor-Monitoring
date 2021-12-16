using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    public class NameNodeComparer : Comparer<NodeViewModel>
    {
        public override int Compare(NodeViewModel x, NodeViewModel y)
        {
            if (x == null && y == null)
                return 0;

            if (x == null)
                return -1;

            if (y == null)
                return 1;

            return x.Name.CompareTo(y.Name);
        }
    }

    public class NameSensorComparer : Comparer<SensorViewModel>
    {
        public override int Compare(SensorViewModel x, SensorViewModel y)
        {
            if (x == null && y == null)
                return 0;

            if (x == null)
                return -1;

            if (y == null)
                return 1;

            return x.Name.CompareTo(y.Name);
        }
    }
}
