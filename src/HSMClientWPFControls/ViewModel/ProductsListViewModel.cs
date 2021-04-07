using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.Model;

namespace HSMClientWPFControls.ViewModel
{
    public class ProductsListViewModel : ViewModelBase
    {
        private readonly IProductsMonitoringModel _productsModel;
        private ProductViewModel _selectedProduct;
        private string _statusText;
        public ProductsListViewModel(IProductsMonitoringModel productsModel) : base(productsModel as ModelBase)
        {
            _productsModel = productsModel;
            //Products = new ObservableCollection<ProductViewModel>();
            //Products.CollectionChanged += Products_CollectionChanged;
            ContextMenuProductCopyProductKey = new SingleDelegateCommand(CopyProductKey);
            AddProductCommand = new MultipleDelegateCommand(AddProduct, CanAddProduct);
            RemoveProductCommand = new RelayCommand<ProductViewModel>(RemoveProduct);
            //DisplayProductsList();
        }

        public ObservableCollection<ProductViewModel> Products => _productsModel?.Products;

        public ICommand RemoveProductCommand { get; private set; }
        public ICommand AddProductCommand { get; private set; }
        public ICommand ContextMenuProductCopyProductKey { get; private set; }
        public ProductViewModel SelectedProduct
        {
            get => _selectedProduct;
            set
            {
                _selectedProduct = value;
                OnPropertyChanged(nameof(SelectedProduct));
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        private void AddProduct()
        {
            _productsModel.AddProduct();
        }

        private bool CanAddProduct()
        {
            return true;
        }

        private void RemoveProduct(ProductViewModel product)
        {
            _productsModel.RemoveProduct(product.Info);
        }

        //private void DisplayProductsList()
        //{
        //    _monitoringModel.UpdateProducts();

        //    StatusText = $"{Products.Count} in list, updated at {DateTime.Now:T}";
        //}

        private bool CopyProductKey(object o, bool isCheckOnly)
        {
            if (isCheckOnly)
                return true;

            Clipboard.SetDataObject(SelectedProduct.Key);
            return true;
        }
    }
}
