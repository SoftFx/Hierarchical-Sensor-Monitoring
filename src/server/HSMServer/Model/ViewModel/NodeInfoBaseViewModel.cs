using HSMServer.Model.TreeViewModel;
using System.Collections.Generic;


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

            ExpectedUpdateInterval = new(model.ExpectedUpdateInterval.ToModel(), model.Parent is null ? _predefinedIntervals.GetRange(1, _predefinedIntervals.Count - 1) : _predefinedIntervals, () =>
            {
                while (model.Parent?.ExpectedUpdateInterval?.TimeInterval is TimeInterval.FromParent)
                    model= model.Parent;
                
                return model.Parent?.ExpectedUpdateInterval;
            });
            SensorRestorePolicy = new(model.SensorRestorePolicy.ToModel(), model.Parent is null ? _predefinedIntervals.GetRange(1, _predefinedIntervals.Count - 1) : _predefinedIntervals, () =>
            {
                while (model.Parent?.SensorRestorePolicy?.TimeInterval is TimeInterval.FromParent)
                    model= model.Parent;
                
                return model.Parent?.SensorRestorePolicy;
            });
        }
    }
}
