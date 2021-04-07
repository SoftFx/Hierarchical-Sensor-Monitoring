using System.Collections.ObjectModel;
using OxyPlot;
using OxyPlot.Series;

namespace HSMClientWPFControls.Model.SensorDialog
{
    public interface IBoolSensorModel : ISensorDialogModel
    {
        ObservableCollection<string> Times { get; set; }
        ObservableCollection<ColumnItem> Data { get; set; }
        int Count { get; set; }
    }
}