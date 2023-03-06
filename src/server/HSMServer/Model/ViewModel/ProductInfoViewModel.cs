using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.ViewModel
{
    public class ProductInfoViewModel : NodeInfoBaseViewModel
    {
        public string Name { get; }

        public NodeViewModel Parent { get; }


        public ProductInfoViewModel() : base() { }

        internal ProductInfoViewModel(ProductNodeViewModel product) : base(product)
        {
            Name = product.Name;
            Parent = product.Parent;
        }
    }
}
