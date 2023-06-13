namespace HSMDataCollector.Logging
{
    public interface ICollectorLogger
    {
        void Debug<T>(T value);

        void Info<T>(T value);

        void Error<T>(T value);
    }
}
