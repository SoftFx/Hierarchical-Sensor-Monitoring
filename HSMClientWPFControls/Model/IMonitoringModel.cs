using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.ViewModel;

namespace HSMClientWPFControls.Model
{
    public interface IMonitoringModel : INotifyPropertyChanged
    {
        public string ConnectionAddress { get; }
        public DateTime LastUpdateTime { get; } 
        event EventHandler ConnectionStatusChanged;
        public bool IsConnected { get; }
        public bool IsClientCertificateDefault { get; }
        public IProductsConnector ProductsConnector { get; }
        public ISensorHistoryConnector SensorHistoryConnector { get; }
        public ISettingsConnector SettingsConnector { get; }
        ObservableCollection<MonitoringNodeBase> Nodes { get; }
        ObservableCollection<ProductViewModel> Products { get; set; }
        void Dispose();
        void ShowProducts();
        void ShowSettingsWindow();
        void ShowGenerateCertificateWindow();
        void MakeNewClientCertificate(CreateCertificateModel model);
        void UpdateProducts();
        void RemoveProduct(ProductInfo product);
        ProductInfo AddProduct(string name);
    }
}
