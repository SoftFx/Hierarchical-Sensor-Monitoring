using System.ComponentModel;
using System.Windows;
using HSMGrpcClient;

namespace HSMClient
{
    /// <summary>
    /// Interaction logic for ProductsWindow.xaml
    /// </summary>
    public partial class ProductsWindow : Window
    {
        private readonly ProductsWindowViewModel _viewModel;
        public ProductsWindow(ConnectorBase connectorBase)
        {
            _viewModel = new ProductsWindowViewModel(connectorBase);
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
