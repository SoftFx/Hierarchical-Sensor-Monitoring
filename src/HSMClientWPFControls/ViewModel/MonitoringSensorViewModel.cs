using System;
using System.Collections.Generic;
using System.Text;
using HSMClient.Common;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.Objects;

namespace HSMClientWPFControls.ViewModel
{
    public class MonitoringSensorViewModel : NotifyingBase
    {
        public DateTime _lastStatusUpdate;
        private MonitoringNodeBase _parent;
        private string _status;
        private string _shortValue;
        private SensorTypes _sensorType;
        private string _message;
        private Dictionary<string, string> _validationParams;
        private string _path;
        private MonitoringSensorUpdate _sensorUpdate;
        public MonitoringSensorViewModel(MonitoringSensorUpdate sensorUpdate, MonitoringNodeBase parent = null)
        {
            _lastStatusUpdate = DateTime.Now;
            Name = sensorUpdate.Name;
            _parent = parent;
            Product = sensorUpdate.Product;
            _status = TextConstants.Error;
            _sensorType = sensorUpdate.SensorType;
            _path = ConvertPathToString(sensorUpdate.Path);
            ShortValue = sensorUpdate.ShortValue;
            _sensorUpdate = sensorUpdate;
        }
        public MonitoringSensorViewModel(string name, MonitoringNodeBase parent = null)
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
        public string Path => _path;
        public MonitoringSensorUpdate SensorUpdate => _sensorUpdate;
        public void Update(MonitoringSensorUpdate sensorUpdate)
        {
            _sensorUpdate = sensorUpdate;
            ShortValue = sensorUpdate.ShortValue;
        }

        private string ConvertPathToString(List<string> path)
        {
            StringBuilder sb = new StringBuilder(path[0]);
            for (int i = 1; i < path.Count; ++i)
            {
                sb.Append($"/{path[i]}");
            }

            return sb.ToString();
        }
    }
}
