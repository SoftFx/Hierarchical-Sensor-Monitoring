using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using HSMClientWPFControls.ViewModel.SensorDialog;

namespace HSMClientWPFControls.SensorExpandingService
{
    public class DialogWindow : Window
    {
        private DialogViewModel _viewModel;

        public DialogWindow(UserControl control, DialogViewModel viewModel, string title = "sensor history")
        {
            Title = title;
            Content = control;
            _viewModel = viewModel;
            DataContext = _viewModel;
            Closing += DialogWindow_Closing;
            SizeToContent = SizeToContent.WidthAndHeight;
        }

        private void DialogWindow_Closing(object sender, CancelEventArgs e)
        {
            Closing -= DialogWindow_Closing;
            _viewModel?.Dispose();
        }
    }
}
