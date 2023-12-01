using System;

namespace HSMDataCollector.Logging
{
    internal interface ILoggerManager : ICollectorLogger
    {
        event Action<string> ThrowNewError;
    }


    internal sealed class LoggerManager : ILoggerManager
    {
        private ICollectorLogger _logger;


        public event Action<string> ThrowNewError;


        internal void AddLogger(ICollectorLogger logger)
        {
            _logger = logger;
        }


        public void Debug(string message) => _logger?.Debug(message);

        public void Info(string message) => _logger?.Info(message);


        public void Error(string message)
        {
            _logger?.Error(message);

            ThrowNewError?.Invoke(message);
        }

        public void Error(Exception ex)
        {
            _logger?.Error(ex);

            ThrowNewError?.Invoke($"{ex}");
        }
    }
}