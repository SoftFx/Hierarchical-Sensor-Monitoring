using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    public class LastTimeUpdateNodeComparer : Comparer<NodeViewModel>
    {
        public override int Compare(NodeViewModel x, NodeViewModel y)
        {
            return y.UpdateTime.CompareTo(x.UpdateTime);
        }
    }

    public class LastTimeUpdateSensorComparer : Comparer<SensorViewModel>
    {
        public override int Compare(SensorViewModel x, SensorViewModel y)
        {
            return y.Time.CompareTo(x.Time);
        }
    }
}
