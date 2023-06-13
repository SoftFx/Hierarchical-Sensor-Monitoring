namespace HSMDataCollector.Logging
{
    public sealed class LoggerOptions
    {
        public ICollectorLogger Logger { get; set; }

        public string ConfigPath { get; set; }

        public bool WriteDebug { get; set; }
    }
}
