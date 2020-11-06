using System.ComponentModel;
using System.Windows;
using HSMClientWPFControls;
using HSMClientWPFControls.ViewModel;

namespace HSMClient
{
    /// <summary>
    /// Interaction logic for ProductsWindow.xaml
    /// </summary>
    public partial class ProductsWindow : Window
    {
        private readonly ProductsWindowViewModel _viewModel;
        public ProductsWindow(IMonitoringModel monitoringModel)
        {
            InitializeComponent();
            _viewModel = new ProductsWindowViewModel(monitoringModel);
            this.DataContext = _viewModel;
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
