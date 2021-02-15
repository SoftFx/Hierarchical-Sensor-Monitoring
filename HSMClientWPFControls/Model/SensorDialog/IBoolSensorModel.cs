using System.Collections.ObjectModel;
using OxyPlot;

namespace HSMClientWPFControls.Model.SensorDialog
{
    public interface IBoolSensorModel : ISensorDialogModel
    {
        ObservableCollection<DataPoint> Data { get; set; }
        int Count { get; set; }
    }
}