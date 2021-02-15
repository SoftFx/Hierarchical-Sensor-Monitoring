using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Threading;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.Objects.TypedSensorData;
using HSMClientWPFControls.ViewModel;
using OxyPlot;
using OxyPlot.Axes;

namespace HSMClient.Dialog
{
    class ClientNumericTimeValueModel : ClientDialogTimerModel, INumericTimeValueModel
    {
        private readonly DateTime _unixOrigin = new DateTime(1970,1,1);
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
                        //if (Data.Count == 0  || point.X > Data.Last().X)
                        //{
                        //    _context.Send(_ => Data.Add(point), null);
                        //}
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

            _data = points;
        }
    }
}
