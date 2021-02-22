using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Navigation;
using HSMClientWPFControls.Bases;

namespace HSMClient
{
    public class DownloadUpdateModel : ModelBase
    {
        private int _downloadProgress = 50;

        public DownloadUpdateModel(ClientMonitoringModel model)
        {

        }
        public int DownloadProgress
        {
            get => _downloadProgress;
            set
            {
                _downloadProgress = value;
                OnPropertyChanged(nameof(DownloadProgress));
            }
        }
        private void StartDownloading()
        {

        }
    }
}
