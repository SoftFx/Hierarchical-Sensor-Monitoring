using HSMCommon.Model;

namespace HSMClientWPFControls.Model
{
    public interface IUpdateClientModel
    {
        bool IsUpdating { get; set; }
        bool IsUpdateAvailable { get; }
        int DownloadProgress { get; }
        int DownloadedFilesCount { get; }
        bool IsUpdateDownloaded { get; }
        ClientUpdateInfoModel UpdateInfo { get; }
        ClientVersionModel UpdateVersion { get; }
        void DownloadUpdate();
    }
}