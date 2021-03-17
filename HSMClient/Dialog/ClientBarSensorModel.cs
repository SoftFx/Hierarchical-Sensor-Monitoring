using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Documents.Serialization;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.ViewModel;
using HSMSensorDataObjects.TypedDataObject;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace HSMClient.Dialog
{
    class ClientBarSensorModel : ClientDialogTimerModel, IBarSensorModel
    {
        private Collection<BoxPlotItem> _items;
        public ClientBarSensorModel(ISensorHistoryConnector connector, MonitoringSensorViewModel sensor) : base(connector, sensor)
        {
            Items = new Collection<BoxPlotItem>();
            Count = 10;
            Title = _path;
        }
        public string Title { get; set; }
        public int Count { get; set; }

        public Collection<BoxPlotItem> Items
        {
            get => _items;
            set
            {
                _items = value;
                OnPropertyChanged(nameof(Items));
            }
        }
        protected override void OnTimerTick()
        {
            List<SensorHistoryItem> list = _connector.GetSensorHistory(_product, _path, _name, Count);

            var boxes = new Collection<BoxPlotItem>();

            list.Reverse();
            var type = list[0].Type;
            switch (type)
            {
                case SensorTypes.BarIntSensor:
                {
                    foreach (var box in list)
                    {
                        var boxValue = JsonSerializer.Deserialize<IntBarSensorData>(box.SensorValue);

                        int firstQuant = boxValue.Percentiles.FirstOrDefault(p => Math.Abs(p.Percentile - 0.25) < double.Epsilon).Value;
                        int thirdQuant = boxValue.Percentiles.FirstOrDefault(p => Math.Abs(p.Percentile - 0.75) < double.Epsilon).Value;
                        int median = boxValue.Percentiles.FirstOrDefault(p => Math.Abs(p.Percentile - 0.5) < double.Epsilon).Value;
                        double lowerWhisker = firstQuant - (int) (1.5 * (thirdQuant - firstQuant));
                        double upperWhisker = thirdQuant - (int)(1.5 * (thirdQuant - firstQuant));

                        BoxPlotItem plotItem = new BoxPlotItem(DateTimeAxis.ToDouble(boxValue.StartTime), lowerWhisker, firstQuant, median,
                            thirdQuant, upperWhisker);

                        boxes.Add(plotItem);
                    }

                    break;
                }
                case SensorTypes.BarDoubleSensor:
                {
                    foreach (var box in list)
                    {
                        var boxValue = JsonSerializer.Deserialize<IntBarSensorData>(box.SensorValue);

                        double firstQuant = boxValue.Percentiles.FirstOrDefault(p => Math.Abs(p.Percentile - 0.25) < double.Epsilon).Value;
                        double thirdQuant = boxValue.Percentiles.FirstOrDefault(p => Math.Abs(p.Percentile - 0.75) < double.Epsilon).Value;
                        double median = boxValue.Percentiles.FirstOrDefault(p => Math.Abs(p.Percentile - 0.5) < double.Epsilon).Value;
                        double lowerWhisker = firstQuant - (int)(1.5 * (thirdQuant - firstQuant));
                        double upperWhisker = thirdQuant - (int)(1.5 * (thirdQuant - firstQuant));

                        BoxPlotItem plotItem = new BoxPlotItem(DateTimeAxis.ToDouble(box.Time), lowerWhisker, firstQuant, median,
                            thirdQuant, upperWhisker);

                        boxes.Add(plotItem);
                    }

                    break;
                    }
            }

            Items = boxes;

        }
    }
}
