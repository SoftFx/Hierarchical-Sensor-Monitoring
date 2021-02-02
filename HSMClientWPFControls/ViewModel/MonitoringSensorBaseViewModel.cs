using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using HSMClient.Common;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.Objects.TypedSensorData;

namespace HSMClientWPFControls.ViewModel
{
    public class MonitoringSensorBaseViewModel : NotifyingBase
    {
        public DateTime _lastStatusUpdate;
        private MonitoringNodeBase _parent;
        private string _status;
        private string _shortValue;
        private SensorTypes _sensorType;
        private object _dataObject;
        private string _message;
        private Dictionary<string, string> _validationParams;
        public MonitoringSensorBaseViewModel(MonitoringSensorUpdate sensorUpdate, MonitoringNodeBase parent = null)
        {
            _lastStatusUpdate = DateTime.Now;
            Name = sensorUpdate.Name;
            _parent = parent;
            Product = sensorUpdate.Product;
            _status = TextConstants.Error;
            _sensorType = sensorUpdate.SensorType;
            _dataObject = sensorUpdate.DataObject;
        }
        public MonitoringSensorBaseViewModel(string name, MonitoringNodeBase parent = null)
        {
            _lastStatusUpdate = DateTime.Now;
            Name = name;
            _parent = parent;
            _status = TextConstants.Error;
        }
        public string Name { get; set; }
        public string Product { get; private set; }
        public MonitoringNodeBase Parent
        {
            get { return _parent; }
            set { _parent = value; }
        }

        public string LastStatusUpdate
        {
            get { return _lastStatusUpdate.ToString(@"G"); }
        }

        public string Status
        {
            get { return _status; }
            set
            {
                if (_status != value)
                    _lastStatusUpdate = DateTime.Now;

                _status = value;
                _parent?.UpdateStatus();
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusDuration));
            }
        }

        public string Message
        {
            get => _message;
            set
            {
                _message = value;
                OnPropertyChanged(nameof(Message));
            }
        }


        public string ShortValue
        {
            get => _shortValue;
            set
            {
                _shortValue = value;
                OnPropertyChanged(nameof(ShortValue));
            }
        }

        public Dictionary<string, string> ValidationParams
        {
            get
            {
                if(_validationParams == null)
                    _validationParams = new Dictionary<string, string>();
                return _validationParams;
            }
            set
            {
                _validationParams = value;
                OnPropertyChanged(nameof(ValidationParams));
            }
        }

        public string StatusDuration
        {
            get
            {
                if (_lastStatusUpdate.ToBinary() == 0)
                    _lastStatusUpdate = DateTime.Now;
                TimeSpan duration = DateTime.Now - _lastStatusUpdate;
                return duration.ToString(@"hh\:mm\:ss");
            }
        }
        public SensorTypes SensorType => _sensorType;
        public object DataObject => _dataObject;

        public void Update(MonitoringSensorUpdate sensorUpdate)
        {
            ShortValue = $"{sensorUpdate.Time:F}. {GetSpecialTypedValue(sensorUpdate)}";
        }

        private string GetSpecialTypedValue(MonitoringSensorUpdate update)
        {
            string stringVal = Encoding.UTF8.GetString(update.DataObject);
            switch (update.SensorType)
            {
                case SensorTypes.BoolSensor:
                {
                    BoolSensorData typedData = JsonSerializer.Deserialize<BoolSensorData>(stringVal);
                    return $"Value = {typedData.BoolValue}";
                }

                case SensorTypes.DoubleSensor:
                {
                    DoubleSensorData typedData = JsonSerializer.Deserialize<DoubleSensorData>(stringVal);
                    return $"Value = '{typedData.DoubleValue}'";
                }

                case SensorTypes.IntSensor:
                {
                    IntSensorData typedData = JsonSerializer.Deserialize<IntSensorData>(stringVal);
                    return $"Value = '{typedData.IntValue}'";
                }

                case SensorTypes.StringSensor:
                {
                    StringSensorData typedData = JsonSerializer.Deserialize<StringSensorData>(stringVal);
                    return $"Value = '{typedData.StringValue}'";
                }

                case SensorTypes.BarDoubleSensor:
                {
                    DoubleBarSensorData typedData = JsonSerializer.Deserialize<DoubleBarSensorData>(stringVal);
                    return
                        $"Value: Min = {typedData.Min}, Max = {typedData.Max}, Mean = {typedData.Mean}, Count = {typedData.Count}";
                }


                case SensorTypes.BarIntSensor:
                {
                    IntBarSensorData typedData = JsonSerializer.Deserialize<IntBarSensorData>(stringVal);
                    return
                        $"Value: Min = {typedData.Min}, Max = {typedData.Max}, Mean = {typedData.Mean}, Count = {typedData.Count}";
                }
            }

            return string.Empty;
        }
    }
}
