using HSMDataCollector.Core;
using NLog;
using NLog.Config;
using System;
using System.IO;

namespace HSMDataCollector.Logging
{
    internal sealed class LoggerManager
    {
        private const string DefaultConfigPath = "collector.nlog.config";


        internal Logger Logger { get; private set; }


        internal void InitializeLogger(LoggerOptions options)
        {
            var configPath = options?.ConfigPath;
            if (string.IsNullOrEmpty(configPath))
                configPath = Path.Combine(AppContext.BaseDirectory, DefaultConfigPath);

            var factory = new LogFactory(new XmlLoggingConfiguration(configPath));

            Logger = factory.GetLogger(nameof(DataCollector));
        }
    }
}
