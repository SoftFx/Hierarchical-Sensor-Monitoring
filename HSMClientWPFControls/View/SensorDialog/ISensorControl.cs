using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.ViewModel.SensorDialog;

namespace HSMClientWPFControls.View.SensorDialog
{
    public interface ISensorControl
    {
        public DialogViewModel ConstructDefaultViewModel(ISensorDialogModel model);
    }
}