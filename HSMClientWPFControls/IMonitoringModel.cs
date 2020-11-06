using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.ViewModel;

namespace HSMClientWPFControls
{
    public interface IMonitoringModel
    {
        ObservableCollection<MonitoringNodeBase> Nodes { get; set; }
        ObservableCollection<ProductViewModel> Products { get; set; }
        void Dispose();
        void ShowProducts();
        public event EventHandler ShowProductsEvent;
        void UpdateProducts();
        void RemoveProduct(ProductInfo product);
        ProductInfo AddProduct(string name);
    }
}
