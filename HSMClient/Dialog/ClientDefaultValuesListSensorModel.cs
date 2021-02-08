using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.ViewModel;

namespace HSMClient.Dialog
{
    class ClientDefaultValuesListSensorModel : ClientDialogModelBase, IDefaultValuesListModel
    {
        public ClientDefaultValuesListSensorModel(ISensorHistoryConnector connector,
            MonitoringSensorViewModel sensor) : base(connector, sensor)
        {
            List = new ObservableCollection<DefaultSensorModel>();
            List.CollectionChanged += List_CollectionChanged;
            UpdateData();
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
        public ObservableCollection<DefaultSensorModel> List { get; set; }

        public string CountText
        {
            get => $"Count = {List.Count}";
            set => OnPropertyChanged(nameof(CountText));
        }
        public void Refresh()
        {
            UpdateData();
        }
    }
}
