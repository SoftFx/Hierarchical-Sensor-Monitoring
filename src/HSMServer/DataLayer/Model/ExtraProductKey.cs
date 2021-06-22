namespace HSMServer.DataLayer.Model
{
    public class ExtraProductKey
    {
        public string Name { get; set; }
        public string Key { get; set; }

        public ExtraProductKey(string name, string key)
        {
            Name = name;
            Key = key;
        }
    }
}
