using HSMServer.Extensions;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using System;
using Telegram.Bot.Types;

namespace HSMServer.Model
{
    public enum NotificationsTarget
    {
        Groups,
        Accounts
    }

    public class IgnoreNotificationsViewModel
    {
        private const string NodeTreeElement = "node";
        private const string SensorTreeElement = "sensor";
        private const string ProductTreeElement = "product";
        private const string FolderTreeElement = "folder";


        public NotificationsTarget NotificationsTarget { get; set; }

        public string Path { get; }

        public string TreeElement { get; }

        public string EncodedId { get; set; }

        public TimeIntervalViewModel IgnorePeriod { get; set; }

        public ChatId Chat { get; set; }

        public int Days { get; set; }

        public int Hours { get; set; }

        public int Minutes { get; set; }

        public DateTime DateTimeNow { get; set; }

        public DateTime EndOfIgnorePeriod => IgnorePeriod.TimeInterval == TimeInterval.Forever ?
                                             DateTime.MaxValue : DateTimeNow.AddDays(Days).AddHours(Hours).AddMinutes(Minutes);

        public bool IsOffTimeModal { get; set; }


        private IgnoreNotificationsViewModel(BaseNodeViewModel node, NotificationsTarget target, bool isOffTimeModal)
        {
            TreeElement = node switch
            {
                SensorNodeViewModel => SensorTreeElement,
                ProductNodeViewModel => NodeTreeElement,
                FolderModel => FolderTreeElement,
                _ => null
            };

            IgnorePeriod = new(PredefinedIntervals.ForIgnore, false);

            DateTimeNow = DateTime.UtcNow.RoundToMin();
            NotificationsTarget = target;
            IsOffTimeModal = isOffTimeModal;
        }

        // public constructor without parameters for action Home/IgnoreNotifications
        public IgnoreNotificationsViewModel() { }

        public IgnoreNotificationsViewModel(NodeViewModel node, NotificationsTarget target, bool isOffTimeModal)
            : this((BaseNodeViewModel)node, target, isOffTimeModal)
        {
            EncodedId = node.EncodedId;
            Path = node.FullPath;

            if (node.Id == node.RootProduct.Id)
                TreeElement = ProductTreeElement;
        }

        public IgnoreNotificationsViewModel(FolderModel folder, NotificationsTarget target, bool isOffTimeModal)
            : this((BaseNodeViewModel)folder, target, isOffTimeModal)
        {
            EncodedId = folder.Id.ToString();
            Path = folder.Name;
        }
    }
}
