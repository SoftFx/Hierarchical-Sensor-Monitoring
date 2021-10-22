using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    public class LastTimeUpdateNodeComparer : Comparer<NodeViewModel>
    {
        public override int Compare(NodeViewModel x, NodeViewModel y)
        {
            //return y.UpdateTime.CompareTo(x.UpdateTime);

            if (x.UpdateTime > y.UpdateTime) return -1;
            else if (x.UpdateTime < y.UpdateTime) return 1;
            else return 0;
        }
    }

    public class LastTimeUpdateSensorComparer : Comparer<SensorViewModel>
    {
        public override int Compare(SensorViewModel x, SensorViewModel y)
        {
            //return y.Time.CompareTo(x.Time);

            if (x.Time > y.Time) return -1;
            else if (x.Time < y.Time) return 1;
            else return 0; 
        }
    }
}
