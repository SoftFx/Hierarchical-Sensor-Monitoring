namespace HSMServer.Model.ViewModel
{
    public class ConfigurationObjectViewModel
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public bool IsDefault { get; set; }
        public string Description { get; set; }

        public ConfigurationObjectViewModel() { }
        //public ConfigurationObjectViewModel(ConfigurationObject obj, bool isDefault)
        //{
        //    IsDefault = isDefault;
        //    Name = obj.Name;
        //    Value = obj.Value;
        //    Description = obj.Description;

        //    if (Name == ConfigurationConstants.SensorExpirationTime)
        //        Name = "Remove sensor data after N days inactivity";
        //}
    }
}
