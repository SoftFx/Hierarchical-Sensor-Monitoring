using System;

namespace HSMDataCollector.Logging
{
    internal sealed class LoggerManager : ICollectorLogger
    {
        private ICollectorLogger _logger;


        internal void AddLogger(ICollectorLogger logger)
        {
            _logger = logger;
        }


        public void Debug(string message) => _logger?.Debug(message);

        public void Info(string message) => _logger?.Info(message);

        public void Error(string message) => _logger?.Error(message);

        public void Error(Exception ex) => _logger?.Error(ex);
    }
}
