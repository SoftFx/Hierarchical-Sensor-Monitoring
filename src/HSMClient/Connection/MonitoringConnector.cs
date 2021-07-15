using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using HSMClient.Common;
using HSMClient.Common.Logging;
using HSMClient.Configuration;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.Model;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.ViewModel;
using HSMCommon;
using HSMSensorDataObjects;

namespace HSMClient.Connection
{
    public class MonitoringConnector : NotifyingBase, IMonitoringConnector
    {
        #region Private fields

        private readonly Dictionary<string, MonitoringNodeBase> _nameToNode;
        private DateTime _lastUpdate = DateTime.MinValue;
        private string _connectionAddress;
        private bool _continue = true;
        private bool _isClientCertificateDefault;
        private readonly SynchronizationContext _uiContext;
        private readonly ConnectorBase _sensorsClient;
        private Thread _treeThread;
        private const int UPDATE_TIMEOUT = 10000;
        private const int CONNECTION_TIMEOUT = 10000;
        private ConnectionStatus _connectionStatus;

        #endregion

        #region Fileds with Notify
        private ConnectionStatus connectionStatus
        {
            get => _connectionStatus;
            set
            {
                _connectionStatus = value;
                OnPropertyChanged(nameof(IsConnected));
            }
        }
        private DateTime lastUpdate
        {
            get => _lastUpdate;
            set
            {
                _lastUpdate = value;
                OnPropertyChanged(nameof(LastUpdate));
            }
        }

        private string connectionAddress
        {
            get => _connectionAddress;
            set
            {
                _connectionAddress = value;
                OnPropertyChanged(nameof(ConnectionAddress));
            }
        }

        private bool isClientCertificateDefault
        {
            get => _isClientCertificateDefault;
            set
            {
                _isClientCertificateDefault = value;
                OnPropertyChanged(nameof(IsClientCertificateDefault));
            }
        }
        #endregion

        #region Public fields

        public ObservableCollection<MonitoringNodeBase> Nodes { get; }

        public DateTime LastUpdate => _lastUpdate;
        public string ConnectionAddress => _connectionAddress;

        public bool IsConnected
        {
            get
            {
                if (_connectionStatus == ConnectionStatus.Error)
                    return false;
                return _connectionStatus == ConnectionStatus.Ok;
            }
        }

        public bool IsClientCertificateDefault => _isClientCertificateDefault;

        #endregion

        #region Public methods

        public void ReplaceClientCertificate(X509Certificate2 clientCertificate)
        {
            _sensorsClient.ReplaceClientCertificate(clientCertificate);
            isClientCertificateDefault = false;
        }

        public void Stop()
        {
            _continue = false;
        }

        public X509Certificate2 GetSignedClientCertificate(CreateCertificateModel model, out X509Certificate2 caCertificate)
        {
            return _sensorsClient.GetSignedClientCertificate(model, out caCertificate);
        }

        public void Restart()
        {
            StartTreeThread();
            isClientCertificateDefault = ConfigProvider.Instance.IsClientCertificateDefault;
        }

        #endregion

        public MonitoringConnector()
        {
            _nameToNode = new Dictionary<string, MonitoringNodeBase>();
            connectionAddress =
                $"{ConfigProvider.Instance.ConnectionInfo.Address}:{ConfigProvider.Instance.ConnectionInfo.Port}";
            _sensorsClient = new GrpcClientConnector(_connectionAddress);
            _uiContext = SynchronizationContext.Current;

            Nodes = new ObservableCollection<MonitoringNodeBase>();

            isClientCertificateDefault = ConfigProvider.Instance.IsClientCertificateDefault;

            StartTreeThread();
        }

        private void StartTreeThread()
        {
            if (_treeThread != null)
            {
                try
                {
                    _treeThread.Interrupt();
                }
                catch (ThreadInterruptedException ex)
                { }
                catch (Exception e)
                {
                    Logger.Error($"Failed to stop working tree thread, error = {e}");
                }
            }
            connectionStatus = ConnectionStatus.Init;
            _treeThread = new Thread(MonitoringLoopStep);
            _treeThread.Name = $"Thread_{DateTime.Now.ToLongTimeString()}";
            _treeThread.Start();
        }

        private void MonitoringLoopStep()
        {
            while (_continue)
            {
                try
                {
                    DateTime stepStart = DateTime.Now;
                    if (_connectionStatus == ConnectionStatus.Init || _connectionStatus == ConnectionStatus.Error)
                    {
                        Connect();
                        if (_connectionStatus == ConnectionStatus.Init ||
                            _connectionStatus == ConnectionStatus.Error)
                            if ((DateTime.Now - stepStart).TotalMilliseconds < CONNECTION_TIMEOUT)
                                Thread.Sleep(Math.Max(0,
                                    CONNECTION_TIMEOUT - (int)((DateTime.Now - stepStart).TotalMilliseconds)));
                    }
                    else
                    {
                        Update();
                        if ((DateTime.Now - stepStart).TotalMilliseconds < UPDATE_TIMEOUT)
                            Thread.Sleep(Math.Max(0,
                                UPDATE_TIMEOUT - (int)((DateTime.Now - stepStart).TotalMilliseconds)));
                    }
                }
                catch (ThreadInterruptedException ex)
                {
                    Logger.Info("Monitoring tree loop step stopped!");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Client monitoring model: MonitoringLoopStep error = {ex}");
                }

            }
        }

        private void Connect()
        {
            try
            {
                if (ConfigProvider.Instance.IsClientCertificateDefault)
                {
                    bool isConnected = _sensorsClient.CheckServerAvailable();
                    connectionStatus = isConnected ? ConnectionStatus.Ok : ConnectionStatus.Error;
                }
                else
                {
                    var responseObj = _sensorsClient.GetTree();
                    connectionStatus = ConnectionStatus.Ok;
                    _lastUpdate = DateTime.Now;
                    Update(responseObj);
                }
            }
            catch (Exception e)
            {
                Logger.Error($"ClientMonitoringModel: Connect error: {e}");
                connectionStatus = ConnectionStatus.Error;
                foreach (var node in Nodes)
                {
                    node.Status = SensorStatus.Error;
                }
            }
        }

        private void Update()
        {
            try
            {
                var responseObj = _sensorsClient.GetUpdates();
                connectionStatus = ConnectionStatus.Ok;
                _lastUpdate = DateTime.Now;
                Update(responseObj);
            }
            catch (Exception e)
            {
                Logger.Error($"ClientMonitoringModel: Update error: {e}");
                connectionStatus = ConnectionStatus.Error;
                foreach (var node in Nodes)
                {
                    node.Status = SensorStatus.Error;
                }
            }
        }

        private void Update(List<MonitoringSensorUpdate> updateList)
        {
            foreach (var sensorUpd in updateList)
            {
                if (!_nameToNode.ContainsKey(sensorUpd.Product))
                {
                    MonitoringNodeBase node = new MonitoringNodeBase(sensorUpd.Product);
                    _nameToNode[sensorUpd.Product] = node;
                    //Dispatcher.CurrentDispatcher.Invoke(delegate { Nodes.Add(node); });
                    _uiContext.Send(x => Nodes.Add(node), null);
                }
                _uiContext.Send(x => _nameToNode[sensorUpd.Product].Update(sensorUpd, 0), null);
                //_nameToNode[sensorUpd.Product].Update(Converter.Convert(sensorUpd), 1);
            }
        }
    }
}
