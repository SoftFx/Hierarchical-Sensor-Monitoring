using HSMClientWPFControls;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.ViewModel;

namespace HSMClient
{
    public class ProductsWindowViewModel : ViewModelBase
    {
        private readonly IMonitoringModel _monitoringModel;
        private ProductsListViewModel _productsViewModel;
        public ProductsWindowViewModel(IMonitoringModel monitoringModel)
        {
            _monitoringModel = monitoringModel;
            _productsViewModel = new ProductsListViewModel(_monitoringModel);
        }

        public ProductsListViewModel ProductsListViewModel
        {
            get => _productsViewModel;
            set => _productsViewModel = value;
        }

    }
}