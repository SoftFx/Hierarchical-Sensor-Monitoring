using System.Windows.Controls;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.ViewModel.SensorDialog;

namespace HSMClientWPFControls.View.SensorDialog
{
    public abstract class SensorControl : UserControl
    {
        public abstract DialogViewModel ConstructDefaultViewModel(ISensorDialogModel model);
    }
}
