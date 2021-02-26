using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using HSMClient.Common;
using HSMClient.Common.Logging;

namespace HSMClient
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        private MainWindowViewModel _model;
        public MainWindow()
        {
            Title = TextConstants.AppName;
            
            try
            {
                _model = new MainWindowViewModel();
                _model.UpdateClient += Model_UpdateClient;
                Logger.Info("MainViewModel created");
            }
            catch (Exception e)
            {
                Logger.Fatal($"Failed to create _model: {e}");
            }
            
            this.DataContext = _model;
            try
            {
                InitializeComponent();
                Logger.Info("InitializeComponent was successful");
            }
            catch (Exception e)
            {
                Logger.Fatal($"Failed to initialize component: {e}");
                throw;
            }

            Closing += MainWindow_Closing;
        }

        private void Model_UpdateClient(object sender, EventArgs e)
        {
            Shutdown();
        }

        private void Shutdown()
        {
            this.Close();
            Application.Current.Shutdown();
        }
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            _model?.Dispose();
        }
    }
}
