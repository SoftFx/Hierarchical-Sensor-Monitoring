using HSMServer.Core.Model;

namespace HSMServer.Model.ViewModel
{
    public class SensorInfoViewModel
    {
        public string Path { get; set; }
        public string ProductName { get; set; }
        public string Description { get; set; }
        public string ExpectedUpdateInterval { get; set; }
        public SensorInfoViewModel(SensorInfo info)
        {
            Path = info.Path;
            ProductName = info.ProductName;
            Description = info.Description;
            ExpectedUpdateInterval = info.ExpectedUpdateInterval.ToString();
        }
    }
}
