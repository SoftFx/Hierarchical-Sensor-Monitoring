using System;
using System.ComponentModel;
using System.Windows.Input;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.Model;

namespace HSMClientWPFControls.ViewModel
{
    public class UpdateClientViewModel : ViewModelBase, IDisposable
    {
        private readonly IUpdateClientModel _updateClientModel;
        public UpdateClientViewModel(IUpdateClientModel model) : base(model as ModelBase)
        {
            _updateClientModel = model;
            RegisterProperty(nameof(IUpdateClientModel.DownloadedFilesCount), nameof(DownloadStatusText));
            RegisterProperty(nameof(IUpdateClientModel.IsUpdating), nameof(IsUpdateDownloading));
            RegisterProperty(nameof(IUpdateClientModel.DownloadProgress), nameof(DownloadProgressValue));
            RegisterProperty(nameof(IUpdateClientModel.IsUpdateDownloaded), nameof(IsDownloadCompleted));
            DownloadButtonCommand = new SingleDelegateCommand(DownloadUpdate);
            InstallButtonCommand = new SingleDelegateCommand(InstallUpdate);
        }

        public ICommand DownloadButtonCommand { get; }
        public ICommand InstallButtonCommand { get; }
        public bool IsUpdateDownloading => _updateClientModel.IsUpdating;
        public bool IsUpdateAvailable => _updateClientModel.IsUpdateAvailable;
        public int DownloadProgressValue => _updateClientModel.DownloadProgress;

        public bool IsDownloadCompleted =>
            _updateClientModel.DownloadedFilesCount == _updateClientModel?.UpdateInfo?.Files?.Count;

        public string DownloadStatusText
        {
            get
            {
                if (IsUpdateDownloading)
                {
                    return $"{_updateClientModel.DownloadedFilesCount}/{_updateClientModel.UpdateInfo.Files.Count} files downloaded";
                }

                return string.Empty;
            }
        }
        

        public string DownloadButtonText => $"Download version {_updateClientModel.UpdateVersion}";
        public string InstallButtonText => $"Install version {_updateClientModel.UpdateVersion}";

        private bool DownloadUpdate(object o, bool isCheckOnly)
        {
            if (isCheckOnly)
                return true;

            _updateClientModel.DownloadUpdate();
            return true;
        }

        private bool InstallUpdate(object o, bool isCheckOnly)
        {
            if (isCheckOnly)
                return true;

            //
            return true;
        }

    }
}
