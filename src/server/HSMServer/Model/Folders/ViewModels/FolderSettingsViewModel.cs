using System;

namespace HSMServer.Model.Folders.ViewModels
{
    public sealed class FolderSettingsViewModel
    {
        public Guid Id { get; set; }

        public TimeIntervalViewModel SelfDestoryPeriod { get; set; }

        public TimeIntervalViewModel SavedHistoryPeriod { get; set; }

        public TimeIntervalViewModel ExpectedUpdateInterval { get; set; }


        public FolderSettingsViewModel() { }

        internal FolderSettingsViewModel(FolderModel folder)
        {
            Id = folder.Id;
            ExpectedUpdateInterval = folder.TTL;
            SelfDestoryPeriod = folder.SelfDestroy;
            SavedHistoryPeriod = folder.KeepHistory;
        }
    }
}
