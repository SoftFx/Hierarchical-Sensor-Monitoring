namespace HSMDataCollector
{
    public class TextConstants
    {
        public const string PerformanceNodeName = "System monitoring";
        public const string CurrentProcessNodeName = "CurrentProcess";
        public const string WindowsUpdateNodeName = "Is need Windows update";

        public const string LogTargetFile = "file";
        public const string LogDefaultFolder = "Logs";
        public const string LogFormatFileName = "DataCollector_${shortdate}.log";//${date:format=yyyy-MM-dd HH-mm-ss}.log
        public const string LogDebug = "Debug";
        public const string LogInfo = "Info";
        public const string LogWarn = "Warn";
        public const string LogError = "Error";
        public const string LogFatal = "Fatal";
        public const string LogTrace = "Trace";

        public const string InstallDate = "InstallDate";
        public const string Version = "Version";
        public const string Win32OperatingSystem = "SELECT * FROM Win32_OperatingSystem";
    }
}
