namespace HSMDataCollector.PublicInterface
{
    public interface IBarSensor<T> where T : struct
    {
        void AddValue(T value);
    }
}