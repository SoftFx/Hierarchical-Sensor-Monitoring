using HSMServer.Attributes;
using HSMServer.Model.DataAlerts;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.ViewModel
{
    public class NodeInfoBaseViewModel // this class can't be abstract because it's used for HomeController.IsMetaInfoValid action
    {
        public ConcurrentDictionary<string, int> AlertIcons { get; }

        public string Header { get; }

        public Guid RootProductId { get; }

        public bool HasTimeToLive { get; }

        public DateTime LastUpdateTime { get; set; }

        public SensorStatus Status { get; set; }

        [Obsolete("Remove after adding TTL constructor for Folder")]
        [Display(Name = "Time to live interval")]
        [MinTimeInterval(TimeInterval.OneMinute, ErrorMessage = "{0} minimal value is {1}.")]
        public TimeIntervalViewModel ExpectedUpdateInterval { get; set; }

        [Display(Name = "Keep sensor history")]
        [MinTimeInterval(TimeInterval.Hour, ErrorMessage = "{0} minimal value is {1}.")]
        public TimeIntervalViewModel SavedHistoryPeriod { get; set; }

        [Display(Name = "Remove sensor after inactivity")]
        [MinTimeInterval(TimeInterval.Hour, ErrorMessage = "{0} minimal value is {1}.")]
        public TimeIntervalViewModel SelfDestroyPeriod { get; set; }

        public Dictionary<byte, List<DataAlertViewModelBase>> DataAlerts { get; set; } = [];

        public SensorHistoryStatisticViewModel HistoryStatistic { get; }


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

            ExpectedUpdateInterval = new(model.TTL, PredefinedIntervals.ForFolderTimeout);
            SavedHistoryPeriod = new(model.KeepHistory, PredefinedIntervals.ForKeepHistory);
            SelfDestroyPeriod = new(model.SelfDestroy, PredefinedIntervals.ForSelfDestory);

            HistoryStatistic = model.HistoryStatistic;

            AlertIcons = model.AlertIcons;
            HasTimeToLive = model.TTL.TimeInterval is not TimeInterval.None;
            DataAlerts = new(model.DataAlerts);
            if (model.TTLAlert is not null)
                DataAlerts[TimeToLiveAlertViewModel.AlertKey] = [model.TTLAlert.FromInterval(model.TTL)];
        }
    }
}
