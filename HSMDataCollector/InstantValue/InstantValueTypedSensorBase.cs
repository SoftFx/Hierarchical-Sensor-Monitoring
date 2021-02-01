namespace HSMDataCollector.InstantValue
{
    abstract class InstantValueTypedSensorBase<T> : InstantValueSensorBase where T : struct
    {
        protected T Value;
        
        protected InstantValueTypedSensorBase(string path, string productKey, string address) : base(path, productKey, address)
        {
        }
    }
}
