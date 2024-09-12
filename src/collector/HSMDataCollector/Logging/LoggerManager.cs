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


        public void Debug(string message)
        {
            try
            {
                _logger?.Debug(message);
            }
            catch { }
        }

        public void Info(string message)
        {
            try
            {
                _logger?.Info(message);
            }
            catch { }
        }


        public void Error(string message)
        {
            try
            {
                _logger?.Error(message);
            }
            catch { }
        }

        public void Error(Exception ex)
        {
            try
            {
                _logger?.Error(ex);
            }
            catch { }
        }

    }
}