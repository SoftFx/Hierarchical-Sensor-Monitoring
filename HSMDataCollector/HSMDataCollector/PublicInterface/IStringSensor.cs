namespace HSMDataCollector.PublicInterface
{
    public interface IStringSensor
    {
        void AddValue(string value);
        void AddValue(string value, string comment);
    }
}