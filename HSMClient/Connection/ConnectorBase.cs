using System.Collections.Generic;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Objects;

namespace HSMClient.Connection
{
    public abstract class ConnectorBase : IProductsConnector, ISensorsTreeConnector, ISensorHistoryConnector,
        ISettingsConnector
    {
        protected string _address;

        protected ConnectorBase(string address)
        {
            _address = address;
        }

        public abstract List<MonitoringSensorUpdate> GetTree();
        public abstract List<MonitoringSensorUpdate> GetUpdates();
        public abstract List<ProductInfo> GetProductsList();
        public abstract ProductInfo AddNewProduct(string name);
        public abstract bool RemoveProduct(string name);
        public abstract List<MonitoringSensorUpdate> GetSensorHistory(string product, string name, long n);
    }
}
