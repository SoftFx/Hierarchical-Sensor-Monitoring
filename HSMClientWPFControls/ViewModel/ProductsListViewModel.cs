using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using HSMClientWPFControls.Bases;

namespace HSMClientWPFControls.ViewModel
{
    public class ProductsListViewModel : ViewModelBase
    {
        private readonly IMonitoringModel _monitoringModel;
        private ProductViewModel _selectedProduct;
        private string _statusText;
        public ProductsListViewModel(IMonitoringModel monitoringModel) : base(monitoringModel as ModelBase)
        {
            _monitoringModel = monitoringModel;
            //Products = new ObservableCollection<ProductViewModel>();
            //Products.CollectionChanged += Products_CollectionChanged;
            ProductDoubleClickCommand = new SingleDelegateCommand(CopyProductKey);
            AddProductCommand = new MultipleDelegateCommand(AddProduct, CanAddProduct);
            DisplayProductsList();
        }

        //private void Products_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        //{
        //    OnPropertyChanged(nameof(StatusText));
        //}

        public ObservableCollection<ProductViewModel> Products => _monitoringModel?.Products;

        public ICommand RemoveProductCommand { get; private set; }
        public ICommand AddProductCommand { get; private set; }
        public ICommand ProductDoubleClickCommand { get; private set; }
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
            //AddNewProductWindow 
        }

        private bool CanAddProduct()
        {
            return true;
        }

        private void DisplayProductsList()
        {
            _monitoringModel.UpdateProducts();

            StatusText = $"{Products.Count} in list, updated at {DateTime.Now:T}";
        }

        private bool CopyProductKey(object o, bool isCheckOnly)
        {
            if (isCheckOnly)
                return true;
            else
            {
                Clipboard.SetDataObject(SelectedProduct.Key);
                return true;
            }
        }
    }
}
