using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using HSMClient.Common;
using HSMClient.Common.Logging;
using HSMClient.Configuration;
using HSMClient.Connection;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.ViewModel;
using HSMCommon;

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

        private bool _isConnected;
        public event EventHandler ShowProductsEvent;
        public event EventHandler ShowSettingsWindowEvent;
        public event EventHandler ShowGenerateCertificateWindowEvent;
        public event EventHandler DefaultCertificateReplacedEvent;
        public void MakeNewClientCertificate(CreateCertificateModel model)
        {
            X509Certificate2 newCertificate = _sensorsClient.GetNewClientCertificate(model);
            ConfigProvider.Instance.UpdateClientCertificate(newCertificate, model.CommonName);
            _sensorsClient.ReplaceClientCertificate(newCertificate);
            StartTreeThread();
            OnDefaultCertificateReplacedEvent();
        }

        public void UpdateProducts()
        {
            var responseObj = _sensorsClient.GetProductsList();
            foreach (var product in responseObj)
            {
                Products.Add(new ProductViewModel(product));
            }
        }

        public void RemoveProduct(ProductInfo product)
        {
            bool res = _sensorsClient.RemoveProduct(product.Name);
            Logger.Info($"Remove product name = {product.Name} result = {res}");
        }

        public ProductInfo AddProduct(string name)
        {
            return _sensorsClient.AddNewProduct(name);
        }

        private readonly ConnectorBase _sensorsClient;
        private Thread _treeThread;
        private const int UPDATE_TIMEOUT = 10000;
        private const int CONNECTION_TIMEOUT = 10000;
        private DateTime _lastUpdate = DateTime.MinValue;
        private bool _continue = true;
        private ConnectionsStatus _connectionsStatus;
        private readonly object _lockObject = new object();
        private readonly Dictionary<string, MonitoringNodeBase> _nameToNode;
        private readonly SynchronizationContext _uiContext;
        public ClientMonitoringModel()
        {
            _nameToNode = new Dictionary<string, MonitoringNodeBase>();
            Nodes = new ObservableCollection<MonitoringNodeBase>();
            Products = new ObservableCollection<ProductViewModel>();
            _sensorsClient = new GrpcClientConnector(
                $"{ConfigProvider.Instance.ConnectionInfo.Address}:{ConfigProvider.Instance.ConnectionInfo.Port}");
            _connectionsStatus = ConnectionsStatus.Init;
            _uiContext = SynchronizationContext.Current;
            StartTreeThread();
        }

        private void MonitoringLoopStep()
        {
            while (_continue)
            {
                try
                {
                    DateTime stepStart = DateTime.Now;
                    if (_connectionsStatus == ConnectionsStatus.Init || _connectionsStatus == ConnectionsStatus.Error)
                    {
                        Connect();
                        if (_connectionsStatus == ConnectionsStatus.Init ||
                            _connectionsStatus == ConnectionsStatus.Error)
                            if ((DateTime.Now - stepStart).TotalMilliseconds < CONNECTION_TIMEOUT)
                                Thread.Sleep(Math.Max(0,
                                    CONNECTION_TIMEOUT - (int) ((DateTime.Now - stepStart).TotalMilliseconds)));
                    }
                    else
                    {
                        Update();
                        if ((DateTime.Now - stepStart).TotalMilliseconds < UPDATE_TIMEOUT)
                            Thread.Sleep(Math.Max(0,
                                UPDATE_TIMEOUT - (int) ((DateTime.Now - stepStart).TotalMilliseconds)));
                    }
                }
                catch (ThreadInterruptedException ex)
                {
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
                if (IsClientCertificateDefault)
                {
                    _isConnected = _sensorsClient.CheckServerAvailable();
                }
                else
                {
                    var responseObj = _sensorsClient.GetTree();
                    _connectionsStatus = ConnectionsStatus.Ok;
                    Update(responseObj);
                }
            }
            catch (Exception e)
            {
                Logger.Error($"ClientMonitoringModel: Connect error: {e}");
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
                var responseObj = _sensorsClient.GetUpdates();
                _connectionsStatus = ConnectionsStatus.Ok;
                Update(responseObj);
            }
            catch (Exception e)
            {
                Logger.Error($"ClientMonitoringModel: Update error: {e}");
                _connectionsStatus = ConnectionsStatus.Error;
                foreach (var node in Nodes)
                {
                    node.Status = TextConstants.UpdateError;
                }
            }
        }

        private void StartTreeThread()
        {
            if (_treeThread != null)
            {
                try
                {
                    _treeThread.Interrupt();
                }
                catch (ThreadInterruptedException exception)
                { }
                catch (Exception e)
                {
                    Logger.Error($"Failed to stop working tree thread, error = {e}");
                }
            }
            _connectionsStatus = ConnectionsStatus.Init;
            _treeThread = new Thread(MonitoringLoopStep);
            _treeThread.Name = $"Thread_{DateTime.Now.ToLongTimeString()}";
            _treeThread.Start();
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
        public ObservableCollection<MonitoringNodeBase> Nodes { get; set; }
        public ObservableCollection<ProductViewModel> Products { get; set; }

        public event EventHandler ConnectionStatusChanged;
        public bool IsConnected => _isConnected;

        public bool IsClientCertificateDefault
        {
            get
            {
                var cert = ConfigProvider.Instance.ConnectionInfo.ClientCertificate;
                return cert?.Thumbprint?.Equals(CommonConstants.DefaultClientCertificateThumbprint,
                    StringComparison.OrdinalIgnoreCase) ?? false;
            }
        }
            
        public ISensorHistoryConnector SensorHistoryConnector => _sensorsClient;
        public IProductsConnector ProductsConnector => _sensorsClient;
        public ISettingsConnector SettingsConnector => _sensorsClient;

        public override void Dispose()
        {
            _continue = false;
        }

        public void ShowProducts()
        {
            OnShowProductsEvent();
        }

        public void ShowSettingsWindow()
        {
            OnShowSettingsWindowEvent();
        }

        public void ShowGenerateCertificateWindow()
        {
            OnShowGenerateCertificateWindowEvent();
        }
        private void OnShowSettingsWindowEvent()
        {
            ShowSettingsWindowEvent?.Invoke(this, EventArgs.Empty);
        }

        private void OnShowProductsEvent()
        {
            ShowProductsEvent?.Invoke(this, EventArgs.Empty);
        }

        private void OnShowGenerateCertificateWindowEvent()
        {
            ShowGenerateCertificateWindowEvent?.Invoke(this, EventArgs.Empty);
        }

        private void OnDefaultCertificateReplacedEvent()
        {
            DefaultCertificateReplacedEvent?.Invoke(this, EventArgs.Empty);
        }

        private void OnConnectionStatusChangedEvent()
        {
            ConnectionStatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
