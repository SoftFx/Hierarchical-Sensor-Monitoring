using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.ViewModel;
using OxyPlot.Series;

namespace HSMClient.Dialog
{
    class ClientBarSensorModel : ClientDialogTimerModel, IBarSensorModel
    {
        private Collection<BoxPlotItem> _items;
        public ClientBarSensorModel(ISensorHistoryConnector connector, MonitoringSensorViewModel sensor) : base(connector, sensor)
        {
            Items = new Collection<BoxPlotItem>();

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
        protected override void OnTimerTick()
        {
            List<SensorHistoryItem> list = _connector.GetSensorHistory(_product, _path, _name, Count);

            
        }
    }
}
