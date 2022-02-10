﻿using NLog;
using NLog.Targets;

namespace HSMDataCollector.Logging
{
    public static class Logger
    {
        //https://github.com/NLog/NLog/wiki/Configure-from-code
        //ToDo: take level from configuration
        public static NLog.Logger Create(string className)
        {
            CreateConfigFile(LogLevel.Trace, LogLevel.Fatal);

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

        public static void CreateConfigFile(LogLevel minLevel, LogLevel maxLevel)
        {
            if (LogManager.Configuration is null)
                LogManager.Configuration = new NLog.Config.LoggingConfiguration();

            if (LogManager.Configuration.FindTargetByName<FileTarget>(TextConstants.LogTargetFile) == null)
            {
                var file = new FileTarget(TextConstants.LogTargetFile)
                {
                    FileName = "${basedir}/logs/DataCollector_${shortdate}.log",
                    Layout = "${longdate} [${uppercase:${level}}] ${logger}: ${message}"
                };

                LogManager.Configuration.AddRule(minLevel, maxLevel, file);
            }
        }
    }
}
