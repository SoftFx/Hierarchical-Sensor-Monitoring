using System;
using System.Collections.ObjectModel;
using HSMClientWPFControls.Objects;

namespace HSMClientWPFControls
{
    public interface IMonitoringModel
    {
        ObservableCollection<MonitoringNodeBase> Nodes { get; set; }
    }
}
