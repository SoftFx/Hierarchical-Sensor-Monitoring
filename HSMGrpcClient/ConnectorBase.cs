using System.Collections.Generic;
using HSMClientWPFControls.Objects;

namespace HSMGrpcClient
{
    public abstract class ConnectorBase
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
    }
}
