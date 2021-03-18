using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.ViewModel;
using HSMSensorDataObjects.TypedDataObject;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace HSMClient.Dialog
{
    public class ClientBoolSensorModel : ClientDialogTimerModel, IBoolSensorModel
    {
        private ObservableCollection<string> _times;
        private ObservableCollection<ColumnItem> _data;
        public ObservableCollection<ColumnItem> Data
        {
            get { return _data; }
            set
            {
                _data = value;
                OnPropertyChanged(nameof(Data));
            }
        }

        public ObservableCollection<string> Times
        {
            get => _times;
            set
            {
                _times = value;
                OnPropertyChanged(nameof(Times));
            }
        }
        public ClientBoolSensorModel(ISensorHistoryConnector connector, MonitoringSensorViewModel sensor) : base(connector, sensor)
        {
            Data = new ObservableCollection<ColumnItem>();
            Times = new ObservableCollection<string>();
            Count = 10;
        }

        protected override void OnTimerTick()
        {
            var list = _connector.GetSensorHistory(_product, _path, _name, Count);
            if (list.Count < 1)
                return;

            //Data.Clear();
            list.Reverse();
            ObservableCollection<ColumnItem> points = new ObservableCollection<ColumnItem>();
            ObservableCollection<string> times = new ObservableCollection<string>();
            foreach (var item in list)
            {
                BoolSensorData typedData = JsonSerializer.Deserialize<BoolSensorData>(item.SensorValue);
                points.Add(new ColumnItem(typedData.BoolValue ? 1 : 0));
                times.Add($"{item.Time.ToShortDateString()}{Environment.NewLine}{item.Time.ToLongTimeString()}");
            }

            Data = points;
            Times = times;
        }
        public int Count { get; set; }
    }
}
