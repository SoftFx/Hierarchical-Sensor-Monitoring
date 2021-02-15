using System.Collections.ObjectModel;
using System.ComponentModel;
using OxyPlot;

namespace HSMClientWPFControls.Model.SensorDialog
{
    public interface INumericTimeValueModel : INotifyPropertyChanged, ISensorDialogModel
    {
        Collection<DataPoint> Data { get; set; }
        int Count { get; set; }
    }
}