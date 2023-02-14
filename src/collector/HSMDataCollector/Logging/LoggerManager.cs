using HSMDataCollector.Core;
using NLog;
using NLog.Config;
using System;
using System.IO;

namespace HSMDataCollector.Logging
{
    internal sealed class LoggerManager
    {
        private static readonly LoggerOptions _defaultOptions = new LoggerOptions().FillConfigPath();


        internal Logger Logger { get; private set; }

        internal bool WriteDebug { get; private set; }


        internal void InitializeLogger(LoggerOptions options)
        {
            options = options?.FillConfigPath() ?? _defaultOptions;

            var factory = new LogFactory(new XmlLoggingConfiguration(options.ConfigPath));

            Logger = factory.GetLogger(nameof(DataCollector));
            WriteDebug = options.WriteDebug;
        }
    }


    internal static class LoggerOptionsExtension
    {
        internal static LoggerOptions FillConfigPath(this LoggerOptions options)
        {
            const string DefaultConfigPath = "collector.nlog.config";

            if (string.IsNullOrEmpty(options.ConfigPath))
                options.ConfigPath = Path.Combine(AppContext.BaseDirectory, DefaultConfigPath);

            return options;
        }
    }
}
