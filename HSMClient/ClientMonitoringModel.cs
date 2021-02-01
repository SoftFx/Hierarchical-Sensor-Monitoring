using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
using HSMCommon.Certificates;

namespace HSMClient
{
    public class ClientMonitoringModel : ModelBase, IMonitoringModel
    {
        #region Private fields

        private readonly ConnectorBase _sensorsClient;
        private Thread _treeThread;
        private const int UPDATE_TIMEOUT = 3000;
        private const int CONNECTION_TIMEOUT = 3000;
        private DateTime _lastUpdate = DateTime.MinValue;
        private bool _continue = true;
        private ConnectionStatus _connectionStatus;
        private readonly object _lockObject = new object();
        private readonly Dictionary<string, MonitoringNodeBase> _nameToNode;
        private readonly SynchronizationContext _uiContext;
        private readonly string _connectionAddress;
        private bool _isClientCertificateDefault;

        #endregion

        #region Fields with notify

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
                OnPropertyChanged(nameof(LastUpdateTime));
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

        #region TODO Functionality with connector

        //private IMonitoringConnector _monitoringConnector;

        //public ClientMonitoringModel()
        //{
        //    _monitoringConnector = new MonitoringConnector();
        //    CheckDefaultCA();
        //}

        //public void MakeNewClientCertificate(CreateCertificateModel model)
        //{
        //    X509Certificate2 caCertificate = default(X509Certificate2);
        //    X509Certificate2 newCertificate = _monitoringConnector.GetSignedClientCertificate(model, out caCertificate);
        //    CertificatesProcessor.AddCertificateToTrustedRootCA(caCertificate);
        //    //var convertedCertWithKey = CertificatesProcessor.AddPrivateKey(newCertificate, subjectKeyPair);
        //    ConfigProvider.Instance.UpdateClientCertificate(newCertificate, model.CommonName);
        //    _monitoringConnector.Restart();
        //}
        //public ObservableCollection<MonitoringNodeBase> Nodes => _monitoringConnector.Nodes;
        //public bool IsConnected => _monitoringConnector.IsConnected;
        //public string ConnectionAddress => _monitoringConnector.ConnectionAddress;
        //public bool IsClientCertificateDefault => _monitoringConnector.IsClientCertificateDefault;
        //public override void Dispose()
        //{
        //    _monitoringConnector.Stop();
        //}

        #endregion

        #region Interface

        #region Methods

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
        #endregion

        #region Public fields
        public ObservableCollection<ProductViewModel> Products { get; set; }
        public ObservableCollection<MonitoringNodeBase> Nodes { get; set; }
        public ISensorHistoryConnector SensorHistoryConnector => _sensorsClient;
        public IProductsConnector ProductsConnector => _sensorsClient;
        public ISettingsConnector SettingsConnector => _sensorsClient;
        public bool IsConnected
        {
            get
            {
                if (_connectionStatus == ConnectionStatus.Error)
                    return false;
                return _connectionStatus == ConnectionStatus.Ok;
            }
        }

        public string ConnectionAddress => _connectionAddress;
        public bool IsClientCertificateDefault => _isClientCertificateDefault;
        public DateTime LastUpdateTime => _lastUpdate;
        #endregion

        #region Event handlers

        public event EventHandler ShowProductsEvent;
        public event EventHandler ShowSettingsWindowEvent;
        public event EventHandler ShowGenerateCertificateWindowEvent;

        #endregion

        #endregion

        public event EventHandler DefaultCertificateReplacedEvent;
        public void MakeNewClientCertificate(CreateCertificateModel model)
        {
            X509Certificate2 caCertificate = default(X509Certificate2);
            X509Certificate2 newCertificate = _sensorsClient.GetSignedClientCertificate(model, out caCertificate);
            CertificatesProcessor.AddCertificateToTrustedRootCA(caCertificate);
            //var convertedCertWithKey = CertificatesProcessor.AddPrivateKey(newCertificate, subjectKeyPair);
            ConfigProvider.Instance.UpdateClientCertificate(newCertificate, model.CommonName);
            _sensorsClient.ReplaceClientCertificate(newCertificate);
            UpdateIsCertificateDefault();
            connectionStatus = ConnectionStatus.Init;
            //StartTreeThread();
            //OnDefaultCertificateReplacedEvent();
        }

        public ClientMonitoringModel()
        {
            _nameToNode = new Dictionary<string, MonitoringNodeBase>();
            Nodes = new ObservableCollection<MonitoringNodeBase>();
            Products = new ObservableCollection<ProductViewModel>();
            _connectionAddress =
                $"{ConfigProvider.Instance.ConnectionInfo.Address}:{ConfigProvider.Instance.ConnectionInfo.Port}";
            UpdateIsCertificateDefault();
            _sensorsClient = new GrpcClientConnector(_connectionAddress);
            connectionStatus = ConnectionStatus.Init;
            _uiContext = SynchronizationContext.Current;
            CheckDefaultCA();

            StartTreeThread();
        }

        #region Private methods

        #region Monitoring loop

        private void MonitoringLoopStep()
        {
            while (_continue)
            {
                try
                {
                    DateTime stepStart = DateTime.Now;
                    if (connectionStatus == ConnectionStatus.Init || connectionStatus == ConnectionStatus.Error)
                    {
                        Connect();
                        if (connectionStatus == ConnectionStatus.Init ||
                            connectionStatus == ConnectionStatus.Error)
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
                if (IsClientCertificateDefault)
                {
                    bool isConnected = _sensorsClient.CheckServerAvailable();
                    connectionStatus = isConnected ? ConnectionStatus.Ok : ConnectionStatus.Error;
                }
                else
                {
                    var responseObj = _sensorsClient.GetTree();
                    connectionStatus = ConnectionStatus.Ok;
                    lastUpdate = DateTime.Now;
                    Update(responseObj);
                }
            }
            catch (Exception e)
            {
                Logger.Error($"ClientMonitoringModel: Connect error: {e}");
                connectionStatus = ConnectionStatus.Error;
                _uiContext.Send(x => Nodes?.Clear(), null);
                _nameToNode.Clear();
            }
        }

        private void Update()
        {
            try
            {
                var responseObj = _sensorsClient.GetUpdates();
                connectionStatus = ConnectionStatus.Ok;
                lastUpdate = DateTime.Now;
                Update(responseObj);
            }
            catch (Exception e)
            {
                Logger.Error($"ClientMonitoringModel: Update error: {e}");
                connectionStatus = ConnectionStatus.Error;
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
            connectionStatus = ConnectionStatus.Init;
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

        #endregion

        private void UpdateIsCertificateDefault()
        {
            isClientCertificateDefault = ConfigProvider.Instance.IsClientCertificateDefault;
        }
        private void CheckDefaultCA()
        {
            if (IsClientCertificateDefault)
            {
                string defaultCAPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    CommonConstants.DefaultCertificatesFolderName,
                    CommonConstants.DefaultCACrtCertificateName);

                X509Certificate2 defaultCA = new X509Certificate2(defaultCAPath);
                CertificatesProcessor.AddCertificateToTrustedRootCA(defaultCA);
            }
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
        #endregion





        public event EventHandler ConnectionStatusChanged;

        //public bool IsConnected
        //{
        //    get
        //    {
        //        if (ConnectionStatus == ConnectionStatus.Error)
        //            return false;
        //        return ConnectionStatus == ConnectionStatus.Ok;
        //    }
        //}
        //public string ConnectionAddress => _connectionAddress;
        //public DateTime LastUpdateTime => _lastUpdate;
        
        //{
        //    get
        //    {
        //        var cert = ConfigProvider.Instance.ConnectionInfo.ClientCertificate;
        //        return cert?.Thumbprint?.Equals(CommonConstants.DefaultClientCertificateThumbprint,
        //            StringComparison.OrdinalIgnoreCase) ?? false;
        //    }
        //}

        public override void Dispose()
        {
            _continue = false;
        }
        
    }
}
