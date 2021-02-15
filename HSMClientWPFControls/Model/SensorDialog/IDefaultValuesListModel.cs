using System.Collections.ObjectModel;

namespace HSMClientWPFControls.Model.SensorDialog
{
    public interface IDefaultValuesListModel : ISensorDialogModel
    {
        ObservableCollection<DefaultSensorModel> List { get; set; }
        int Count { get; set; }
        void Refresh();
    }
}