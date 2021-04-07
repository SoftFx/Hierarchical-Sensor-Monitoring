using System.Collections.ObjectModel;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.ViewModel;

namespace HSMClientWPFControls.Model
{
    public interface IProductsMonitoringModel
    {
        ObservableCollection<ProductViewModel> Products { get; set; }
        void Dispose();
        void RemoveProduct(ProductInfo product);
        void AddProduct();
    }
}