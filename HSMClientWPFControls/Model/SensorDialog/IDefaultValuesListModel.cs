using System.Collections.ObjectModel;
using HSMClientWPFControls.ViewModel;

namespace HSMClientWPFControls.Model.SensorDialog
{
    public interface IDefaultValuesListModel : ISensorDialogModel
    {
        ObservableCollection<DefaultSensorModel> List { get; set; }
        string CountText { get; set; }
        void Refresh();
    }
}