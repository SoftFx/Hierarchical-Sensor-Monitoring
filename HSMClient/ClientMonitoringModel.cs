using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HSMClient.Configuration;
using HSMClient.ConnectionNode;
using HSMClientWPFControls;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.Objects;
using MAMSClient.Configuration;

namespace HSMClient
{
    public class ClientMonitoringModel : ModelBase, IMonitoringModel
    {
        private List<MachineInfo> _connectionsList;

        public ClientMonitoringModel()
        {
            Nodes = new ObservableCollection<MonitoringNodeBase>();

            _connectionsList = new List<MachineInfo>();
            _connectionsList.AddRange(ConfigProvider.Instance.MachineInfos);

            Display(_connectionsList);
        }

        private void Display(List<MachineInfo> connectionsList)
        {
            foreach (var machineInfo in connectionsList)
            {
                MachineMonitoringNode node = new MachineMonitoringNode(machineInfo, this);
                Nodes.Add(node);
            }
        }
        public ObservableCollection<MonitoringNodeBase> Nodes { get; set; }
        public void ShowConnectionProperties(MonitoringNodeBase node)
        {
            throw new NotImplementedException();
        }

        public void RemoveConnectionByName(string name)
        {
            throw new NotImplementedException();
        }

        public void AddConnection()
        {
            throw new NotImplementedException();
        }
    }
}
