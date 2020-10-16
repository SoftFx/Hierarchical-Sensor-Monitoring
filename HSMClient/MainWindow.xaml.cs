using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using HSMClient;
using HSMClient.Common;
using HSMClient.Common.Logging;

namespace HSMClient
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainWindowViewModel _model;
        public MainWindow()
        {
            Title = TextConstants.AppName;
            Logger.InitializeLogger();
            Logger.Info("Logger initialized");
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;

            try
            {
                _model = new MainWindowViewModel();
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

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            _model.Dispose();
            //ConfigProvider.Instance.SaveConfig();
        }

        private void Current_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            MessageBox.Show(e.Exception.Message, TextConstants.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.ExceptionObject.ToString(), TextConstants.AppName, MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
