using System.Collections.ObjectModel;
using OxyPlot.Series;

namespace HSMClientWPFControls.Model.SensorDialog
{
    public interface IBarSensorModel : ISensorDialogModel
    {
        public string Title { get; set; }
        int Count { get; set; }
        Collection<BoxPlotItem> Items { get; set; }
    }
}
