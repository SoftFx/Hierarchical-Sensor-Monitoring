namespace HSMClient.Common
{
    public class TextConstants
    {
        public const string AppName = "HSMClient";
        public const string Error = "Error";
        public const string Warning = "Warning";
        public const string Ok = "Ok";
        public const string UpdateError = "Update error";
        //public const string Unknown = "Unknown";
        public const string CompletedText = "completed";
        public const string FailedText = "failed";
        public const string SuccessfulConnectionText = "Connection is successful";
        public const string BadConnectionText = "Failed to connect to server!";
        public const string UpdateFileName = "HSMClientUpdater.exe";
        public const string ClientExeFileName = "HSMClient.exe";

        public static string GetUpdaterLaunchArgs(string appDirectory, string updateDirectory)
        {
            return
                $"-client {AppName} -upd {UpdateFileName} -clientExe {ClientExeFileName} -appDir {appDirectory} -updDir {updateDirectory}";
        }
    }
}
