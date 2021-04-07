using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HSMClient.Common.Logging;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.ViewModel;

namespace HSMClient.Model
{
    public class ClientProductsModel : ModelBase, IProductsMonitoringModel
    {
        private IProductsConnector _connector;
        private readonly List<ProductInfo> _currentProducts;
        private readonly object _lockObj = new object();
        public ObservableCollection<ProductViewModel> Products { get; set; }
        private Dictionary<string, ProductViewModel> _nameToProduct;
        public ClientProductsModel(IProductsConnector connector)
        {
            _connector = connector;
            _currentProducts = new List<ProductInfo>();
            Products = new ObservableCollection<ProductViewModel>();
            _nameToProduct = new Dictionary<string, ProductViewModel>();
            UpdateProducts();
        }
        public override void Dispose()
        {
            _connector = null;
        }

        public void RemoveProduct(ProductInfo product)
        {
            var result = _connector.RemoveProduct(product.Name);
            Logger.Info($"Product '{product.Name}' removal result = {result}");
            UpdateProducts();
        }

        public void AddProduct()
        {
            OnAddNewProductEvent();
        }
        public void AddProduct(string name)
        {
            _connector.AddNewProduct(name);
            UpdateProducts();
        }

        public event EventHandler AddNewProductEvent;
        public event EventHandler<string> RemoveProductEvent;

        private void UpdateProducts()
        {
            var list = _connector.GetProductsList();
            lock (_lockObj)
            {
                _currentProducts.Clear();
            }
            Products.Clear();
            foreach (var product in list)
            {
                Products.Add(new ProductViewModel(product));
                lock (_lockObj)
                {
                    _currentProducts.Add(product);
                }
            }
        }

        private void OnAddNewProductEvent()
        {
            AddNewProductEvent?.Invoke(this, EventArgs.Empty);
        }

        private void OnRemoveProductEvent(string name)
        {
            RemoveProductEvent?.Invoke(this, name);
        }
    }
}
