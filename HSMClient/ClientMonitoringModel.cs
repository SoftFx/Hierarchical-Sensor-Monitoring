using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using HSMClient.Common;
using HSMClient.Configuration;
using HSMClient.Connections;
using HSMClient.Connections.gRPC;
using HSMClientWPFControls;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.Objects;
using SensorsService;

namespace HSMClient
{
    public class ClientMonitoringModel : ModelBase, IMonitoringModel
    {
        #region Connection

        public enum ConnectionsStatus
        {
            Init,
            Error,
            Ok
        }

        #endregion

        private ConnectorBase _sensorsClient;
        private Thread _nodeThread;
        private const int UPDATE_TIMEOUT = 5000;
        private const int CONNECTION_TIMEOUT = 5000;
        private DateTime _lastUpdate = DateTime.MinValue;
        private bool _continue = true;
        private ConnectionsStatus _connectionsStatus;
        private readonly object _lockObject = new object();
        private Dictionary<string, MonitoringNodeBase> _nameToNode;
        public ClientMonitoringModel()
        {
            _nameToNode = new Dictionary<string, MonitoringNodeBase>();
            Nodes = new ObservableCollection<MonitoringNodeBase>();
            _sensorsClient =
                new GrpcClient(
                    $"{ConfigProvider.Instance.ConnectionInfo.Address}:{ConfigProvider.Instance.ConnectionInfo.Port}");
            _connectionsStatus = ConnectionsStatus.Init;
            _nodeThread = new Thread(MonitoringLoopStep);
            _nodeThread.Name = $"Thread_{DateTime.Now.ToLongTimeString()}";
            _nodeThread.Start();
        }

        private void MonitoringLoopStep()
        {
            while (_continue)
            {
                DateTime stepStart = DateTime.Now;
                if (_connectionsStatus == ConnectionsStatus.Init || _connectionsStatus == ConnectionsStatus.Error)
                {
                    Connect();
                    if (_connectionsStatus == ConnectionsStatus.Init || _connectionsStatus == ConnectionsStatus.Error)
                        if ((DateTime.Now - stepStart).TotalMilliseconds < CONNECTION_TIMEOUT)
                            Thread.Sleep(Math.Max(0, CONNECTION_TIMEOUT - (int)((DateTime.Now - stepStart).TotalMilliseconds)));
                }
                else
                {
                    Update();
                    if ((DateTime.Now - stepStart).TotalMilliseconds < UPDATE_TIMEOUT)
                        Thread.Sleep(Math.Max(0, UPDATE_TIMEOUT - (int)((DateTime.Now - stepStart).TotalMilliseconds)));
                }
            }
        }
        private void Connect()
        {
            try
            {
                var responseObj = _sensorsClient.GetTree();

            }
            catch (Exception e)
            {
                _connectionsStatus = ConnectionsStatus.Error;
                foreach (var node in Nodes)
                {
                    node.Status = TextConstants.UpdateError;
                }
            }
        }

        private void Update()
        {
            try
            {

            }
            catch (Exception e)
            {
                _connectionsStatus = ConnectionsStatus.Error;
                foreach (var node in Nodes)
                {
                    node.Status = TextConstants.UpdateError;
                }
            }
        }

        private void Update(SensorsUpdateMessage updateMessage)
        {
            foreach (var sensorUpd in updateMessage.Sensors)
            {
                if (!_nameToNode.ContainsKey(sensorUpd.Product))
                {
                    MonitoringNodeBase node = new MonitoringNodeBase(sensorUpd.Product);
                    _nameToNode[sensorUpd.Product] = node;
                    Nodes.Add(node);
                }
                _nameToNode[sensorUpd.Product].Update(Converter.Convert(sensorUpd), 1);
            }
        }
        public ObservableCollection<MonitoringNodeBase> Nodes { get; set; }

        public override void Dispose()
        {
            _continue = false;
        }
    }
}
