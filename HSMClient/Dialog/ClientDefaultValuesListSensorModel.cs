using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.ViewModel;

namespace HSMClient.Dialog
{
    class ClientDefaultValuesListSensorModel : ClientDialogTimerModel, IDefaultValuesListModel
    {
        public int Count { get; set; }
        public ClientDefaultValuesListSensorModel(ISensorHistoryConnector connector,
            MonitoringSensorViewModel sensor) : base(connector, sensor)
        {
            List = new ObservableCollection<DefaultSensorModel>();
            List.CollectionChanged += List_CollectionChanged;
        }

        private void List_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(List));
        }

        private void UpdateData()
        {
            List.Clear();
            var sensorValues = _connector.GetSensorHistory(_product, _path, _name,-1);
            sensorValues.ForEach(v => List.Add(new DefaultSensorModel(v)));
            OnPropertyChanged(nameof(CountText));
        }

        private ObservableCollection<DefaultSensorModel> _list;

        public ObservableCollection<DefaultSensorModel> List
        {
            get => _list;
            set
            {
                _list = value;
                OnPropertyChanged();
            }
        }

        public string CountText
        {
            get => $"Count = {List.Count}";
            set => OnPropertyChanged(nameof(CountText));
        }
        public void Refresh()
        {
            UpdateData();
        }

        protected override void OnTimerTick()
        {
            var list = _connector.GetSensorHistory(_product, _path, _name, -1);
            var sensorModelList = list.Select(i => new DefaultSensorModel(i)).ToList();
            var observable = new ObservableCollection<DefaultSensorModel>();
            foreach (var sensor in sensorModelList)
            {
                observable.Add(sensor);
            }
            List = observable;
            Count = observable.Count;
            OnPropertyChanged(nameof(CountText));
        }
    }
}
