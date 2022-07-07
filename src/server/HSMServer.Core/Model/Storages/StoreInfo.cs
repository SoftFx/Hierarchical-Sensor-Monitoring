namespace HSMServer.Core.Model.Storages
{
    public readonly struct StoreInfo
    {
        public readonly string Path;
        public readonly string Key;
        public readonly BaseValue Value;


        public StoreInfo(string path, string key, BaseValue value)
        {
            Path = path;
            Key = key;
            Value = value;
        }
    }
}
