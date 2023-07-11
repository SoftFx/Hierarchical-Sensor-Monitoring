using HSMDataCollector.Core;
using NLog;
using NLog.Config;
using System;
using System.IO;

namespace HSMDataCollector.Logging
{
    internal sealed class NLogLogger : ICollectorLogger
    {
        internal const string DefaultConfigPath = "collector.nlog.config";

        private static readonly LoggerOptions _defaultOptions = new LoggerOptions().FillConfigPath();

        private readonly Logger _logger;
        private readonly bool _writeDebug;


        internal NLogLogger(LoggerOptions options)
        {
            options = options?.FillConfigPath() ?? _defaultOptions;

            var factory = new LogFactory(new XmlLoggingConfiguration(options.ConfigPath));

            _logger = factory.GetLogger(nameof(DataCollector));
            _writeDebug = options.WriteDebug;
        }


        public void Debug(string message)
        {
            if (_writeDebug)
                _logger.Debug(message);
        }

        public void Info(string message) => _logger.Info(message);

        public void Error(string message) => _logger.Error(message);

        public void Error(Exception ex) => _logger.Error(ex);
    }


    internal static class LoggerOptionsExtension
    {
        internal static LoggerOptions FillConfigPath(this LoggerOptions options)
        {
            if (string.IsNullOrEmpty(options.ConfigPath))
                options.ConfigPath = Path.Combine(AppContext.BaseDirectory, NLogLogger.DefaultConfigPath);

            return options;
        }
    }
}
