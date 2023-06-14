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

        private Logger _logger;
        private bool _writeDebug;


        internal ICollectorLogger InitializeLogger(LoggerOptions options)
        {
            options = options?.FillConfigPath() ?? _defaultOptions;

            var factory = new LogFactory(new XmlLoggingConfiguration(options.ConfigPath));

            _logger = factory.GetLogger(nameof(DataCollector));
            _writeDebug = options.WriteDebug;

            return this;
        }


        public void Debug<T>(T value)
        {
            if (_writeDebug)
                _logger.Debug(value);
        }

        public void Info<T>(T value) => _logger.Info(value);

        public void Error<T>(T value) => _logger.Error(value);
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
