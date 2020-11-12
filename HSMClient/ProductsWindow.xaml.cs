using System.ComponentModel;
using System.Windows;
using HSMClientWPFControls.ConnectorInterface;

namespace HSMClient
{
    /// <summary>
    /// Interaction logic for ProductsWindow.xaml
    /// </summary>
    public partial class ProductsWindow : Window
    {
        private readonly ProductsWindowViewModel _viewModel;
        public ProductsWindow(IProductsConnector productsConnector)
        {
            _viewModel = new ProductsWindowViewModel(productsConnector);
            this.DataContext = _viewModel;
            InitializeComponent();
        }

        private void ShowProductsList()
        {
            //var list = 
        }


        private void ProductsWindow_Closing(object sender, CancelEventArgs e)
        {
            this.Owner = null;
        }
    }
}
