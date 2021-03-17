namespace HSMDataCollector.PublicInterface
{
    public interface IIntSensor
    {
        void AddValue(int value);
        void AddValue(int value, string comment);
    }
}