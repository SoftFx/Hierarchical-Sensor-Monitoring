using HSMServer.Model.TreeViewModel;


namespace HSMServer.Model.ViewModel
{
    public abstract class NodeInfoBaseViewModel
    {
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

            ExpectedUpdateInterval = new(model.ExpectedUpdateInterval, PredefinedTimeIntervals.ExpectedUpdatePolicy);
            SensorRestorePolicy = new(model.SensorRestorePolicy, PredefinedTimeIntervals.RestorePolicy);
        }
    }
}
