using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    public class LastTimeUpdateNodeComparer : Comparer<NodeViewModel>
    {
        public override int Compare(NodeViewModel x, NodeViewModel y)
        {
            if (x == null && y == null)
                return 0;
            if (x == null && y == null)
                return 0;

            if (x == null)
                return -1;

            if (y == null)
                return 1;

            return x.UpdateTime.CompareTo(y.UpdateTime);
        }
    }

    public class LastTimeUpdateSensorComparer : Comparer<SensorViewModel>
    {
        public override int Compare(SensorViewModel x, SensorViewModel y)
        {
            if (x == null && y == null)
                return 0;

            if (x.Time > y.Time) return -1;
            else if (x.Time < y.Time) return 1;
            else return 0; 
            if (x == null)
                return -1;

            if (y == null)
                return 1;

            return x.Time.CompareTo(y.Time);        }
    }
}
