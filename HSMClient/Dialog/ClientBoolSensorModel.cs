using System.Collections.ObjectModel;
using System.Text.Json;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.ViewModel;
using HSMSensorDataObjects.TypedDataObject;
using OxyPlot;
using OxyPlot.Axes;

namespace HSMClient.Dialog
{
    public class ClientBoolSensorModel : ClientDialogTimerModel, IBoolSensorModel
    {
        private ObservableCollection<DataPoint> _data;
        public ObservableCollection<DataPoint> Data
        {
            get { return _data; }
            set
            {
                _data = value;
                OnPropertyChanged();
            }
        }
        public ClientBoolSensorModel(ISensorHistoryConnector connector, MonitoringSensorViewModel sensor) : base(connector, sensor)
        {
            Data = new ObservableCollection<DataPoint>();
            Count = 10;
        }

        protected override void OnTimerTick()
        {
            var list = _connector.GetSensorHistory(_product, _path, _name, Count);
            if (list.Count < 1)
                return;

            Data.Clear();
            foreach (var item in list)
            {
                BoolSensorData typedData = JsonSerializer.Deserialize<BoolSensorData>(item.SensorValue);
                Data.Add(DateTimeAxis.CreateDataPoint(item.Time, typedData.BoolValue ? 1 : 0));
            }
        }
        public int Count { get; set; }
    }
}
