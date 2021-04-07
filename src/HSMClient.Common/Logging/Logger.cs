using log4net;
using log4net.Config;

namespace HSMClient.Common.Logging
{
    public static class Logger
    {
        private static readonly ILog _log = LogManager.GetLogger("LOGGER");

        public static ILog Log => _log;

        public static void InitializeLogger()
        {
            XmlConfigurator.Configure();
        }

        public static void Info(string message)
        {
            _log.Info(message);
        }

        public static void Info(string message, params object[] args)
        {
            _log.InfoFormat(message, args);
        }

        public static void Debug(string message)
        {
            _log.Debug(message);
        }

        public static void Debug(string message, params object[] args)
        {
            _log.DebugFormat(message, args);
        }

        public static void Warn(string message)
        {
            _log.Warn(message);
        }

        public static void Warn(string message, params object[] args)
        {
            _log.WarnFormat(message, args);
        }

        public static void Error(string message)
        {
            _log.Error(message);
        }

        public static void Error(string message, params object[] args)
        {
            _log.ErrorFormat(message, args);
        }

        public static void Fatal(string message)
        {
            _log.Fatal(message);
        }

        public static void Fatal(string message, params object[] args)
        {
            _log.FatalFormat(message, args);
        }
    }
}
