namespace HSMServer.Model.Folders.ViewModels
{
    public sealed class FolderAlertsViewModel
    {
        public TimeIntervalViewModel ExpectedUpdateInterval { get; set; }

        public TimeIntervalViewModel SensorRestorePolicy { get; set; }


        public FolderAlertsViewModel() { }

        internal FolderAlertsViewModel(FolderModel folder)
        {
            ExpectedUpdateInterval = folder.ExpectedUpdateInterval;
            SensorRestorePolicy = folder.SensorRestorePolicy;
        }
    }
}
