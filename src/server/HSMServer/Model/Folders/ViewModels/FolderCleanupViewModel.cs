using System;

namespace HSMServer.Model.Folders.ViewModels
{
    public class FolderCleanupViewModel
    {
        public Guid Id { get; set; }

        public TimeIntervalViewModel SelfDestoryPeriod { get; set; }

        public TimeIntervalViewModel SavedHistoryPeriod { get; set; }


        public FolderCleanupViewModel() { }

        internal FolderCleanupViewModel(FolderModel folder)
        {
            Id = folder.Id;
            SelfDestoryPeriod = folder.SelfDestroyPeriod;
            SavedHistoryPeriod = folder.SavedHistoryPeriod;
        }
    }
}
