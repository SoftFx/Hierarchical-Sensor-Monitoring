using System;
using System.Linq;
using System.Windows;
using HSMClient.Common;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.ViewModel;
using HSMGrpcClient;

namespace HSMClient
{
    public class ProductsWindowViewModel : ViewModelBase
    {
        private readonly ClientProductsModel _productsClientModel;
        private ProductsListViewModel _productsViewModel;
        public ProductsWindowViewModel(ConnectorBase connector)
        {
            _productsClientModel = new ClientProductsModel(connector);
            _productsViewModel = new ProductsListViewModel(_productsClientModel);

            _productsClientModel.AddNewProductEvent += ProductsClientModel_AddNewProduct;
            _productsClientModel.RemoveProductEvent += ProductsClientModel_RemoveProduct;
        }

        private void ProductsClientModel_RemoveProduct(object sender, string e)
        {
            var result = MessageBox.Show($"Are you sure want to remove product '{e}'", TextConstants.AppName);
            if (result == MessageBoxResult.Cancel || result == MessageBoxResult.No || result == MessageBoxResult.None)
            {
                return;
            }

            var infoToRemove = _productsClientModel.Products.FirstOrDefault(p => p.Name == e)?.Info;
            if (infoToRemove != null)
            {
                _productsClientModel.RemoveProduct(infoToRemove);
            }
        }

        private void ProductsClientModel_AddNewProduct(object sender, EventArgs e)
        {
            AddNewProductWindow window = new AddNewProductWindow(_productsClientModel.Products.Select(p => p.Name).ToList());
            
            bool? dialogResult = window.ShowDialog();
            if (!(dialogResult ?? false))
            {
                return;
            }

            string name = window.NewProductName;
            if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("The name is empty!");
            }
            
            _productsClientModel.AddProduct(name);
        }

        public ProductsListViewModel ProductsListViewModel
        {
            get => _productsViewModel;
            set => _productsViewModel = value;
        }

    }
}