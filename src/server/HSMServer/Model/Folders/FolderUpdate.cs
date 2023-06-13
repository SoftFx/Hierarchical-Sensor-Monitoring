using HSMServer.ConcurrentStorage;
using HSMServer.Notification.Settings;
using System;
using System.Drawing;

namespace HSMServer.Model.Folders
{
    public record FolderUpdate : IUpdateModel
    {
        public required Guid Id { get; init; }


        public TimeIntervalViewModel ExpectedUpdateInterval { get; init; }

        public TimeIntervalViewModel SavedHistoryPeriod { get; init; }

        public TimeIntervalViewModel RestoreInterval { get; init; }

        public TimeIntervalViewModel SelfDestroy { get; init; }


        public NotificationSettings Notifications { get; init; }

        public string Description { get; init; }

        public Color? Color { get; init; }

        public string Name { get; init; }
    }
}
