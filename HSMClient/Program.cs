using System;
using System.Windows;
using System.Windows.Threading;
using HSMClient.Common;
using HSMClient.Common.Logging;

namespace HSMClient
{
    /// <summary>
    /// The app  entry point.
    /// </summary>
    static class Program
    {
        [STAThread]
        static void Main()
        {
            //Initialize log4net logger
            Logger.InitializeLogger();

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            App app = new App();
            app.DispatcherUnhandledException += Current_DispatcherUnhandledException;
            app.Exit += App_Exit;

            Logger.Info($"HSMClient app started at {DateTime.Now:G}");
            app.Run();

            Logger.Info($"HSMClient app stopped at {DateTime.Now:G}");
        }

        private static void App_Exit(object sender, ExitEventArgs e)
        {
            App app = (App) sender;
            app.ReleaseMutex();
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Logger.Error($"{e}");
            MessageBox.Show(e.ExceptionObject.ToString(), TextConstants.AppName, MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private static void Current_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            Logger.Error($"{e}");
            MessageBox.Show(e.Exception.Message, TextConstants.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
