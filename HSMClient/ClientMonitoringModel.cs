using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HSMClient.Configuration;
using HSMClient.ConnectionNode;
using HSMClientWPFControls;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.Objects;

namespace HSMClient
{
    public class ClientMonitoringModel : ModelBase, IMonitoringModel
    {
        public ClientMonitoringModel()
        {
            Nodes = new ObservableCollection<MonitoringNodeBase>();
        }

        public ObservableCollection<MonitoringNodeBase> Nodes { get; set; }
    }
}
