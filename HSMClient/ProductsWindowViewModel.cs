using System;
using System.Linq;
using System.Windows;
using HSMClient.Common;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.ViewModel;

namespace HSMClient
{
    public class ProductsWindowViewModel : ViewModelBase
    {
        #region IDisposable implementation

        // Disposed flag.
        private bool _disposed;

        // Implement IDisposable.
        //public new void Dispose()
        //{
        //    Dispose(true);
        //    GC.SuppressFinalize(this);
        //}

        protected override void Dispose(bool disposingManagedResources)
        {
            // The idea here is that Dispose(Boolean) knows whether it is 
            // being called to do explicit cleanup (the Boolean is true) 
            // versus being called due to a garbage collection (the Boolean 
            // is false). This distinction is useful because, when being 
            // disposed explicitly, the Dispose(Boolean) method can safely 
            // execute code using reference type fields that refer to other 
            // objects knowing for sure that these other objects have not been 
            // finalized or disposed of yet. When the Boolean is false, 
            // the Dispose(Boolean) method should not execute code that 
            // refer to reference type fields because those objects may 
            // have already been finalized."

            if (!_disposed)
            {
                if (disposingManagedResources)
                {

                    // Dispose managed resources here...
                }

                // Dispose unmanaged resources here...

                // Set large fields to null here...

                // Mark as disposed.
                _productsViewModel?.Dispose();
                _productsClientModel?.Dispose();
                _disposed = true;
            }
        }

        // Use C# destructor syntax for finalization code.
        ~ProductsWindowViewModel()
        {
            // Simply call Dispose(false).
            Dispose(false);
        }

        #endregion
        private readonly ClientProductsModel _productsClientModel;
        private ProductsListViewModel _productsViewModel;
        public ProductsWindowViewModel(IProductsConnector connector)
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