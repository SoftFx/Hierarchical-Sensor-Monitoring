using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using HSMClient;
using HSMClient.Common;

namespace MAMSClient
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
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;

            try
            {
                _model = new MainWindowViewModel();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
                throw;
            }
            
            this.DataContext = _model;
            try
            {
                InitializeComponent();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
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
