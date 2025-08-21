using System;
using NLog;
using HSMDataCollector.Logging;


namespace HSMServer.BackgroundServices
{
    internal class DataCollectorLoggerWrapper : ICollectorLogger
    {
        private Logger _logger;

        public DataCollectorLoggerWrapper(Logger logger)
        {
            _logger = logger;
        }

        public void Debug(string message)
        {
            _logger.Debug(message);
        }

        public void Error(string message)
        {
            _logger?.Error(message);
        }

        public void Error(Exception ex)
        {
            _logger.Error(ex);
        }

        public void Info(string message)
        {
            _logger.Info(message);
        }
    }
}
