using HSMServer.Attributes;
using HSMServer.Extensions;
using HSMServer.Model.DataAlerts;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SensorType = HSMServer.Core.Model.SensorType;

namespace HSMServer.Model.ViewModel
{
    public class NodeInfoBaseViewModel
    {
        public string Header { get; }

        public Guid RootProductId { get; }

        public DateTime LastUpdateTime { get; set; }

        public SensorStatus Status { get; set; }

        [Display(Name = "Time to live interval")]
        [MinTimeInterval(TimeInterval.OneMinute, ErrorMessage = "{0} minimal value is {1}.")]
        public TimeIntervalViewModel ExpectedUpdateInterval { get; set; }

        [Display(Name = "Sensitivity interval")]
        [MinTimeInterval(TimeInterval.OneMinute, ErrorMessage = "{0} minimal value is {1}.")]
        public TimeIntervalViewModel SensorRestorePolicy { get; set; }

        [Display(Name = "Keep sensor history")]
        [MinTimeInterval(TimeInterval.Hour, ErrorMessage = "{0} minimal value is {1}.")]
        public TimeIntervalViewModel SavedHistoryPeriod { get; set; }

        [Display(Name = "Remove sensor after inactivity")]
        [MinTimeInterval(TimeInterval.Hour, ErrorMessage = "{0} minimal value is {1}.")]
        public TimeIntervalViewModel SelfDestroyPeriod { get; set; }

        public Dictionary<SensorType, List<DataAlertViewModel>> DataAlerts { get; set; }

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
            Status = model.Status.ToEmpty(model.UpdateTime != DateTime.MinValue);
            Description = model.Description;
            LastUpdateTime = model.UpdateTime;

            ExpectedUpdateInterval = new(model.ExpectedUpdateInterval, PredefinedIntervals.ForTimeout);
            SensorRestorePolicy = new(model.SensorRestorePolicy, PredefinedIntervals.ForRestore);
            SavedHistoryPeriod = new(model.SavedHistoryPeriod, PredefinedIntervals.ForKeepHistory);
            SelfDestroyPeriod = new(model.SelfDestroyPeriod, PredefinedIntervals.ForSelfDestory);

            DataAlerts = model.DataAlerts;
        }
    }
}
