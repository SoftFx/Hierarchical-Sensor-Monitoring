using HSMDataCollector.Core;
using NLog;
using NLog.Config;
using System;
using System.IO;

namespace HSMDataCollector.Logging
{
    internal sealed class LoggerManager : ICollectorLogger
    {
        internal const string DefaultConfigPath = "collector.nlog.config";

        private static readonly LoggerOptions _defaultOptions = new LoggerOptions().FillConfigPath();

        private ICollectorLogger _customLogger;
        private Logger _collectorLogger;
        private bool _writeDebug;

        private ICollectorLogger Logger => _customLogger ?? this;


        internal void InitializeLogger(LoggerOptions options)
        {
            options = options?.FillConfigPath() ?? _defaultOptions;

            var factory = new LogFactory(new XmlLoggingConfiguration(options.ConfigPath));

            _collectorLogger = factory.GetLogger(nameof(DataCollector));
            _writeDebug = options.WriteDebug;
        }

        internal void AddCustomLogger(ICollectorLogger logger)
        {
            _customLogger = logger;
            _writeDebug = false;
        }


        public void Debug(string message)
        {
            if (_writeDebug)
                Logger?.Debug(message);
        }

        public void Info(string message) => Logger?.Info(message);

        public void Error(string message) => Logger?.Error(message);

        public void Error(Exception ex) => Logger?.Error(ex);
    }


    internal static class LoggerOptionsExtension
    {
        internal static LoggerOptions FillConfigPath(this LoggerOptions options)
        {
            if (string.IsNullOrEmpty(options.ConfigPath))
                options.ConfigPath = Path.Combine(AppContext.BaseDirectory, LoggerManager.DefaultConfigPath);

            return options;
        }
    }
}
