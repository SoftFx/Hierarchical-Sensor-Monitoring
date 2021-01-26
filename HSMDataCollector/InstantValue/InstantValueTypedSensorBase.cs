namespace HSMDataCollector.InstantValue
{
    abstract class InstantValueTypedSensorBase<T> : InstantValueSensorBase where T : struct
    {
        protected T Value;
        
        protected InstantValueTypedSensorBase(string name, string path, string productKey, string address) : base(name, path, productKey, address)
        {
        }
    }
}
