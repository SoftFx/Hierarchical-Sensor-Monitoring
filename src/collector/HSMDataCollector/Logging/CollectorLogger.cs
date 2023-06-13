using HSMDataCollector.Core;
using NLog;
using NLog.Config;
using System;
using System.IO;

namespace HSMDataCollector.Logging
{
    internal sealed class CollectorLogger : ICollectorLogger
    {
        internal const string DefaultConfigPath = "collector.nlog.config";

        private readonly Logger _logger;

        private static readonly LoggerOptions _defaultOptions = new LoggerOptions().FillConfigPath();


        internal CollectorLogger(LoggerOptions options)
        {
            options = options?.FillConfigPath() ?? _defaultOptions;

            var factory = new LogFactory(new XmlLoggingConfiguration(options.ConfigPath));

            _logger = factory.GetLogger(nameof(DataCollector));
        }


        public void Debug<T>(T value) => _logger.Debug(value);

        public void Info<T>(T value) => _logger.Info(value);

        public void Error<T>(T value) => _logger.Error(value);
    }


    internal static class LoggerOptionsExtension
    {
        internal static LoggerOptions FillConfigPath(this LoggerOptions options)
        {
            if (string.IsNullOrEmpty(options.ConfigPath))
                options.ConfigPath = Path.Combine(AppContext.BaseDirectory, CollectorLogger.DefaultConfigPath);

            return options;
        }
    }
}
