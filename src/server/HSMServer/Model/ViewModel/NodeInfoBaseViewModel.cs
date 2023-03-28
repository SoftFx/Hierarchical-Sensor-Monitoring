using HSMServer.Model.TreeViewModel;
using System.Collections.Generic;
using HSMServer.Core.Model.Policies;


namespace HSMServer.Model.ViewModel
{
    public abstract class NodeInfoBaseViewModel
    {
        protected static readonly List<TimeInterval> _predefinedIntervals =
            new()
            {
                TimeInterval.FromParent,
                TimeInterval.None,
                TimeInterval.TenMinutes,
                TimeInterval.Hour,
                TimeInterval.Day,
                TimeInterval.Week,
                TimeInterval.Month,
                TimeInterval.Custom
            };


        public string Path { get; }

        public string ProductName { get; }


        public TimeIntervalViewModel ExpectedUpdateInterval { get; set; }

        public TimeIntervalViewModel SensorRestorePolicy { get; set; }


        public string EncodedId { get; set; }

        public string Description { get; set; }


        public NodeInfoBaseViewModel() { }

        internal NodeInfoBaseViewModel(NodeViewModel model)
        {
            Path = model.Path;
            ProductName = model.RootProduct.Name;
            EncodedId = model.EncodedId;
            Description = model.Description;

            ExpectedUpdateInterval = new(model.ExpectedUpdateInterval.ToModel(), _predefinedIntervals,this, model.GetParentInterval(nameof(ExpectedUpdateIntervalPolicy)));
            SensorRestorePolicy = new(model.SensorRestorePolicy.ToModel(), _predefinedIntervals,this ,model.GetParentInterval(nameof(RestoreSensorPolicyBase)));
        }
    }

    public static class TimeIntervalExtension
    {
        public static string GetParentInterval(this NodeViewModel model, string policy)
        {
            while (true)
            {
                if (model.Parent is null) 
                    return null;

                switch (policy)
                {
                    case nameof(RestoreSensorPolicyBase):
                        if (model.Parent.SensorRestorePolicy.TimeInterval is not TimeInterval.FromParent) return model.Parent.SensorRestorePolicy.DisplayInterval;
                        break;
                    case nameof(ExpectedUpdateIntervalPolicy):
                        if (model.Parent.ExpectedUpdateInterval.TimeInterval is not TimeInterval.FromParent) return model.Parent.ExpectedUpdateInterval.DisplayInterval;
                        break;
                }

                model = model.Parent;
            }
        }
    }
}
