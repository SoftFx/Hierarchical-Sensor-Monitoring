using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
using System.Threading;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.ViewModel;
using HSMSensorDataObjects.TypedDataObject;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace HSMClient.Dialog
{
    class ClientNumericTimeValueModel : ClientDialogTimerModel, INumericTimeValueModel
    {
        private Collection<DataPoint> _data;
        private SynchronizationContext _context;
        public Collection<DataPoint> Data
        {
            get { return _data; }
            set
            {
                _data = value;
                OnPropertyChanged();
            }
        }
        public int Count { get; set; }
        public ClientNumericTimeValueModel(ISensorHistoryConnector connector, MonitoringSensorViewModel sensor)
            : base(connector, sensor)
        {
            _context = SynchronizationContext.Current;
            Data = new Collection<DataPoint>();
            Count = 10;
        }

        private void Data_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Data));
        }

        protected override void OnTimerTick()
        {
            var list = _connector.GetSensorHistory(_product, _path, _name, Count);
            if (list.Count < 1)
                return;

            var type = list[0].Type;
            Collection<DataPoint> points = new Collection<DataPoint>();
            list.Reverse();
            switch (type)
            {
                case SensorTypes.IntSensor:
                {
                    foreach (var item in list)
                    {
                        IntSensorData typedData = JsonSerializer.Deserialize<IntSensorData>(item.SensorValue);
                        var point = DateTimeAxis.CreateDataPoint(item.Time, typedData.IntValue);
                        points.Add(point);
                    }
                    break;
                    }
                case SensorTypes.DoubleSensor:
                {
                    foreach (var item in list)
                    {
                        DoubleSensorData typedData = JsonSerializer.Deserialize<DoubleSensorData>(item.SensorValue);
                        Data.Add(DateTimeAxis.CreateDataPoint(item.Time, typedData.DoubleValue));
                    }
                    break;
                }
                default:
                    break;
                    
            }

            Data = points;
        }
    }
}
