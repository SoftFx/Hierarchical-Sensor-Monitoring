using HSMServer.Extensions;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using System;

namespace HSMServer.Model
{
    public class IgnoreNotificationsViewModel
    {
        private const string NodeTreeElement = "node";
        private const string SensorTreeElement = "sensor";
        private const string ProductTreeElement = "product";
        private const string FolderTreeElement = "folder";


        public string Path { get; }

        public string TreeElement { get; }

        public string EncodedId { get; set; }

        public TimeIntervalViewModel IgnorePeriod { get; set; }

        public int Days { get; set; }

        public int Hours { get; set; }

        public int Minutes { get; set; }

        public DateTime DateTimeNow { get; set; }

        public DateTime EndOfIgnorePeriod => IgnorePeriod.TimeInterval is TimeInterval.Forever ?
                                             DateTime.MaxValue : DateTimeNow.AddDays(Days).AddHours(Hours).AddMinutes(Minutes);


        private IgnoreNotificationsViewModel(BaseNodeViewModel node)
        {
            TreeElement = node switch
            {
                SensorNodeViewModel => SensorTreeElement,
                ProductNodeViewModel => NodeTreeElement,
                FolderModel => FolderTreeElement,
                _ => null
            };

            IgnorePeriod = new(PredefinedIntervals.ForIgnore, useCustomTemplate: false);

            DateTimeNow = DateTime.UtcNow.RoundToMin();
        }

        // public constructor without parameters for action Home/IgnoreNotifications
        public IgnoreNotificationsViewModel() { }

        public IgnoreNotificationsViewModel(NodeViewModel node) : this((BaseNodeViewModel)node)
        {
            EncodedId = node.EncodedId;
            Path = node.FullPath;

            if (node.Id == node.RootProduct.Id)
                TreeElement = ProductTreeElement;
        }

        public IgnoreNotificationsViewModel(FolderModel folder) : this((BaseNodeViewModel)folder)
        {
            EncodedId = folder.Id.ToString();
            Path = folder.Name;
        }
    }
}
