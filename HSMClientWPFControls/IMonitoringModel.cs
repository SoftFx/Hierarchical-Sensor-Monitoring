using System;
using System.Collections.ObjectModel;
using HSMClientWPFControls.Objects;

namespace HSMClientWPFControls
{
    public interface IMonitoringModel
    {
        ObservableCollection<MonitoringNodeBase> Nodes { get; set; }

        void ShowConnectionProperties(MonitoringNodeBase node);

        void RemoveConnectionByName(string name);

        void AddConnection();
    }
}
