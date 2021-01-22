namespace HSMDataCollector.Base
{
    public abstract class SensorBase : ISensor
    {
        protected readonly string Name;
        protected readonly string Path;
        protected readonly string ProductKey;
        protected readonly string ServerAddress;
        protected SensorBase(string name, string path, string productKey, string serverAddress)
        {
            Name = name;
            Path = path;
            ProductKey = productKey;
            ServerAddress = serverAddress;
        }

        public abstract void AddValue(object value);
    }
}
