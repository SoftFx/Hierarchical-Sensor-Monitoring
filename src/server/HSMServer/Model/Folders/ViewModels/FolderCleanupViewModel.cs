﻿using System;

namespace HSMServer.Model.Folders.ViewModels
{
    public sealed class FolderCleanupViewModel
    {
        public Guid Id { get; set; }

        public TimeIntervalViewModel SelfDestoryPeriod { get; set; }

        public TimeIntervalViewModel SavedHistoryPeriod { get; set; }


        public FolderCleanupViewModel() { }

        internal FolderCleanupViewModel(FolderModel folder)
        {
            Id = folder.Id;
            SelfDestoryPeriod = folder.SelfDestroy;
            SavedHistoryPeriod = folder.KeepHistory;
        }
    }
}
