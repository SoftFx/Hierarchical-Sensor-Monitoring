using System.Collections.Generic;
using System.Collections.ObjectModel;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.ViewModel;
using OxyPlot;

namespace HSMClient.Dialog
{
    public abstract class ClientNumericTimeValueModel : ClientDialogTimerModel, INumericTimeValueModel
    {
        private Collection<DataPoint> _data;
        public Collection<DataPoint> Data
        {
            get => _data;
            set
            {
                _data = value;
                OnPropertyChanged();
            }
        }
        public int Count { get; set; }

        protected ClientNumericTimeValueModel(ISensorHistoryConnector connector, MonitoringSensorViewModel sensor)
            : base(connector, sensor)
        {
            Data = new Collection<DataPoint>();
            Count = 10;
        }
        
        protected override void OnTimerTick()
        {
            var list = _connector.GetSensorHistory(_product, _path, _name, Count);
            if (list.Count < 1)
                return;
            
            Data = ConvertToDataPoints(list);
        }

        protected abstract Collection<DataPoint> ConvertToDataPoints(List<SensorHistoryItem> historyItems);
    }
}
