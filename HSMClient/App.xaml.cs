using System.Threading;
using System.Windows;
using HSMClient.Common;

namespace HSMClient
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private Mutex _appMutex;
        protected override void OnStartup(StartupEventArgs e)
        {
            this.ShutdownMode = ShutdownMode.OnMainWindowClose;
            bool isOnlyInstance = false;
            _appMutex = new Mutex(true, TextConstants.AppName, out isOnlyInstance);
            if (!isOnlyInstance)
            {
                MessageBox.Show($"App instance is already running.");
                Current.Shutdown();
            }


            base.OnStartup(e);

            MainWindow mainWindow = new MainWindow();
            this.MainWindow = mainWindow;
            mainWindow.Show();
        }

        public void ReleaseMutex()
        {
            _appMutex.Dispose();
            _appMutex = null;
        }
    }
}
