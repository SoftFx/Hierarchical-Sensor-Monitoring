using HSMClientWPFControls.Bases;
using HSMClientWPFControls.Model.SensorDialog;

namespace HSMClientWPFControls.ViewModel.SensorDialog
{
    public class DialogViewModel : ViewModelBase
    {
        public DialogViewModel(ISensorDialogModel model) : base(model as ModelBase)
        { }
    }
}
