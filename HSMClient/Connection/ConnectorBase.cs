using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model;
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

        public abstract DateTime CheckServerAvailable();
        public abstract List<MonitoringSensorUpdate> GetTree();
        public abstract List<MonitoringSensorUpdate> GetUpdates();
        public abstract List<ProductInfo> GetProductsList();
        public abstract ProductInfo AddNewProduct(string name);
        public abstract bool RemoveProduct(string name);
        public abstract List<MonitoringSensorUpdate> GetSensorHistory(string product, string name, long n);
        public abstract X509Certificate2 GetNewClientCertificate(CreateCertificateModel model);

        public abstract void ReplaceClientCertificate(X509Certificate2 certificate);
    }
}
