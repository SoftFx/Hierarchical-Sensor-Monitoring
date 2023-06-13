namespace HSMDataCollector.Logging
{
    internal sealed class LoggerManager
    {
        internal ICollectorLogger Logger { get; private set; }

        internal bool WriteDebug { get; private set; }


        internal void InitializeLogger(LoggerOptions options)
        {
            Logger = options.Logger ?? new CollectorLogger(options);
            WriteDebug = options.WriteDebug;
        }
    }
}
