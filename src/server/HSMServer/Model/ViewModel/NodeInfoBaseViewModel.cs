using HSMServer.Model.DataAlerts;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    public abstract class NodeInfoBaseViewModel
    {
        public string Header { get; }

        public Guid RootProductId { get; }

        public DateTime LastUpdateTime { get; set; }

        public SensorStatus Status { get; set; }

        public TimeIntervalViewModel ExpectedUpdateInterval { get; set; }

        public TimeIntervalViewModel SensorRestorePolicy { get; set; }

        public List<DataAlertViewModel> DataAlerts { get; set; }


        public string EncodedId { get; set; }

        public string Description { get; set; }


        public NodeInfoBaseViewModel() { }

        internal NodeInfoBaseViewModel(NodeViewModel model) : this((BaseNodeViewModel)model)
        {
            EncodedId = model.EncodedId;
            Header = $"{model.RootProduct.Name}{model.Path}";
            RootProductId = model.RootProduct.Id;
        }

        internal NodeInfoBaseViewModel(FolderModel model) : this((BaseNodeViewModel)model)
        {
            EncodedId = model.Id.ToString();
            Header = model.Name;
        }

        private NodeInfoBaseViewModel(BaseNodeViewModel model)
        {
            Status = model.Status;
            Description = model.Description;
            LastUpdateTime = model.UpdateTime;

            ExpectedUpdateInterval = new(model.ExpectedUpdateInterval, PredefinedIntervals.ForTimeout);
            SensorRestorePolicy = new(model.SensorRestorePolicy, PredefinedIntervals.ForRestore);

            DataAlerts = new() { new IntegerDataAlertViewModel() { IsModify = false } };
        }
    }
}
