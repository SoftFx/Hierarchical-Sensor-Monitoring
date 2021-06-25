using HSMServer.DataLayer.Model;

namespace HSMServer.Model.ViewModel
{
    public class ExtraKeyViewModel
    {
        public string ExtraProductKey { get; set; }
        public string ProductKey { get; set; }

        public string ExtraKeyName { get; set; }

        public ExtraKeyViewModel(string productKey, ExtraProductKey extraKey)
        {
            ExtraProductKey = extraKey.Key;
            ExtraKeyName = extraKey.Name;
            ProductKey = productKey;
        }
    }
}
