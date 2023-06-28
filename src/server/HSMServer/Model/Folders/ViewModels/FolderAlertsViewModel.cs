using System;

namespace HSMServer.Model.Folders.ViewModels
{
    public sealed class FolderAlertsViewModel
    {
        public Guid Id { get; set; }

        public TimeIntervalViewModel ExpectedUpdateInterval { get; set; }

        public TimeIntervalViewModel SensorRestorePolicy { get; set; }


        public FolderAlertsViewModel() { }

        internal FolderAlertsViewModel(FolderModel folder)
        {
            Id = folder.Id;
            ExpectedUpdateInterval = folder.ExpectedUpdateInterval;
            //SensorRestorePolicy = folder.SensorRestorePolicy;
        }
    }
}