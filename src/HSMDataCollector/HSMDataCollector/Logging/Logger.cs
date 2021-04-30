using NLog;
using NLog.Targets;

namespace HSMDataCollector.Logging
{
    public static class Logger
    {
        //https://github.com/NLog/NLog/wiki/Configure-from-code
        //ToDo: take level from configuration
        public static NLog.Logger Create(string className)
        {
            GenerateConfigFile(LogLevel.Trace, LogLevel.Fatal);

            return LogManager.GetLogger(className);
        }

        //Use correct format folderPath!
        public static void UpdateFilePath(string folderPath, string fileFormat)
        {
            var configuration = LogManager.Configuration;
            var fileTarget = configuration.FindTargetByName<FileTarget>(TextConstants.LogTargetFile);
            fileTarget.FileName = $@"{folderPath}/" + fileFormat;
            LogManager.Configuration = configuration;
        }

        public static void GenerateConfigFile(LogLevel minLevel, LogLevel maxLevel)
        {
            var config = new NLog.Config.LoggingConfiguration();
            var file = new FileTarget(TextConstants.LogTargetFile)
            {
                FileName = "${basedir}/logs/${shortdate}.log",
                Layout = "${longdate} [${uppercase:${level}}] ${logger}: ${message}"
            };

            config.AddRule(minLevel, maxLevel, file);
          
            LogManager.Configuration = config;
        }
    }
}
