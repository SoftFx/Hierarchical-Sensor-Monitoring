namespace HSMDataCollector.PublicInterface
{
    public interface IBoolSensor
    {
        void AddValue(bool value);
        void AddValue(bool value, string comment);
    }
}