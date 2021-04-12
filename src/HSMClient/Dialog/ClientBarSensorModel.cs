using System.Collections.Generic;
using System.Collections.ObjectModel;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.ViewModel;
using LiveCharts.Defaults;
using OxyPlot.Series;

namespace HSMClient.Dialog
{
    public abstract class ClientBarSensorModel : ClientDialogTimerModel, IBarSensorModel
    {
        private Collection<BoxPlotItem> _items;
        private Collection<OhlcPoint> _points;
        private Collection<string> _labels;
        private Collection<DefaultSensorModel> _defaultList;

        protected ClientBarSensorModel(ISensorHistoryConnector connector, MonitoringSensorViewModel sensor) : base(connector, sensor)
        {
            Items = new Collection<BoxPlotItem>();
            Points = new Collection<OhlcPoint>();
            Labels = new Collection<string>();
            Count = 5;
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

        public Collection<DefaultSensorModel> DefaultList
        {
            get => _defaultList;
            set
            {
                _defaultList = value;
                OnPropertyChanged(nameof(DefaultList));
            }
        }

        public Collection<OhlcPoint> Points
        {
            get => _points;
            set
            {
                _points = value;
                OnPropertyChanged(nameof(Points));
            }
        }

        public Collection<string> Labels
        {
            get => _labels;
            set
            {
                _labels = value;
                OnPropertyChanged(nameof(Labels));
            }
        }
        //protected override void OnTimerTick()
        //{
        //    List<SensorHistoryItem> list = _connector.GetSensorHistory(_product, _path, _name, Count);
        //    if (list.Count < 1)
        //        return;

        //    list.Reverse();
        //    var type = list[0].Type;
        //    Collection<OhlcPoint> points = new Collection<OhlcPoint>();
        //    Collection<string> labels = new Collection<string>();
        //    switch (type)
        //    {
        //        case SensorTypes.BarIntSensor:
        //        {
        //            var serializedList = list.Select(l => JsonSerializer.Deserialize<IntBarSensorData>(l.SensorValue))
        //                .ToList();
        //            List<IntBarSensorData> finalList = new List<IntBarSensorData>();
        //            for (int i = 0; i < serializedList.Count; ++i)
        //            {
        //                if (i < serializedList.Count - 1)
        //                {
        //                    if (serializedList[i].StartTime != serializedList[i + 1].StartTime)
        //                    {
        //                        finalList.Add(serializedList[i]);
        //                        continue;
        //                    }

        //                    if (serializedList[i].EndTime >= serializedList[i + 1].EndTime)
        //                    {
        //                        finalList.Add(serializedList[i]);
        //                        ++i;
        //                    }
        //                    else
        //                    {
        //                        finalList.Add(serializedList[i + 1]);
        //                        i += 2;
        //                    }

        //                    break;
        //                }
        //            }

        //            foreach (var item in finalList)
        //            {
        //                int firstQuant = item.Percentiles.FirstOrDefault(p => Math.Abs(p.Percentile - 0.25) < double.Epsilon).Value;
        //                int thirdQuant = item.Percentiles.FirstOrDefault(p => Math.Abs(p.Percentile - 0.75) < double.Epsilon).Value; 
        //                points.Add(new OhlcPoint(thirdQuant, item.Max, thirdQuant, item.Min));
        //                labels.Add($"{item.StartTime:T}");
        //            }
        //            break;
        //        }
        //    }

        //    Points = points;
        //    Labels = labels;
        //}
        protected override void OnTimerTick()
        {
            List<SensorHistoryItem> list = _connector.GetSensorHistory(_product, _path, _name, Count);
            if (list.Count < 1)
                return;

            Items = ConvertToBoxPlotItems(list);

        }

        protected abstract Collection<BoxPlotItem> ConvertToBoxPlotItems(List<SensorHistoryItem> historyItems);
    }
}
