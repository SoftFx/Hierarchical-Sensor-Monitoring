using System.Collections.ObjectModel;
//using LiveCharts.Defaults;
using OxyPlot.Series;

namespace HSMClientWPFControls.Model.SensorDialog
{
    public interface IBarSensorModel : ISensorDialogModel
    {
        public string Title { get; set; }
        int Count { get; set; }
        Collection<BoxPlotItem> Items { get; set; }
        Collection<DefaultSensorModel> DefaultList { get; set; }
        //Collection<OhlcPoint> Points { get; set; }
        Collection<string> Labels { get; set; }
    }
}
