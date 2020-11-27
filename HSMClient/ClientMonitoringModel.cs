using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        public event EventHandler ShowProductsEvent;
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
        private Thread _nodeThread;
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
                _connectionsStatus = ConnectionsStatus.Ok;
                Update(responseObj);
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

        public override void Dispose()
        {
            _continue = false;
        }

        public void ShowProducts()
        {
            OnShowProductsEvent();
        }

        private void OnShowProductsEvent()
        {
            ShowProductsEvent?.Invoke(this, EventArgs.Empty);
        }
    }
}
