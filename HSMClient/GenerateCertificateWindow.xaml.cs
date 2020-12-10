using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using HSMClientWPFControls.Model;

namespace HSMClient
{
    /// <summary>
    /// Interaction logic for GenerateCertificateWindow.xaml
    /// </summary>
    public partial class GenerateCertificateWindow : System.Windows.Window
    {
        private readonly GenerateCertificateWindowViewModel _viewModel;
        public GenerateCertificateWindow(IMonitoringModel monitoringModel)
        {
            _viewModel = new GenerateCertificateWindowViewModel(monitoringModel);
            this.DataContext = _viewModel;
            InitializeComponent();
        }

        private void buttonGenerate_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
