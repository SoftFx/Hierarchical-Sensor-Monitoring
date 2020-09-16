using System;
using System.Threading;
using System.Windows;
using HSMClient.Connections;
using HSMClientWPFControls;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.UpdateObjects;

namespace HSMClient.ConnectionNode
{
    abstract class OneConnectionMonitoringNode : MonitoringNodeBase
    {
        #region IDisposable implementation

        // Disposed flag.
        private bool _disposed;

        protected override void Dispose(bool disposingManagedResources)
        {
            if (!_disposed)
            {
                if (disposingManagedResources)
                {
                    // Dispose managed resources here...
                    _continue = false;
                    _nodeThread.Join(TimeSpan.FromMilliseconds(50));

                    //if (!result)
                    //{
                    //    _nodeThread.Abort("Application closed");
                    //}
                }




                // Dispose unmanaged resources here...

                // Set large fields to null here...

                // Mark as disposed.
                _disposed = true;
            }

            // Call Dispose in the base class.
            base.Dispose(disposingManagedResources);
        }

        // The derived class does not have a Finalize method
        // or a Dispose method without parameters because it inherits
        // them from the base class.

        #endregion
        public class Connection
        {
            public enum ConnectionsStatus
            {
                Init,
                Error,
                Ok
            }
            public ConnectionsStatus Status { get; set; }
            public Connection() { this.Status = ConnectionsStatus.Init; }
        }

        Connection _connection;
        private ConnectorBase _client;
        private object _lockObject;
        private int _updateTimeout;
        private Thread _nodeThread;
        private bool _continue;
        private object _currentInput;
        private string _address;
        protected IMonitoringCounterStatusHandler Handler;
        public ConnectorBase Client
        {
            get { return _client; }
            set { _client = value; }
        }

        private void NodeLoopStep()
        {
            while (_continue)
            {
                DateTime stepStart = DateTime.Now;
                if (_connection.Status == Connection.ConnectionsStatus.Init || _connection.Status == Connection.ConnectionsStatus.Error)
                {
                    Connect();
                    if (_connection.Status == Connection.ConnectionsStatus.Init || _connection.Status == Connection.ConnectionsStatus.Error)
                        if ((DateTime.Now - stepStart).TotalMilliseconds < _updateTimeout)
                            Thread.Sleep(Math.Max(0, _updateTimeout - (int)(DateTime.Now - stepStart).TotalMilliseconds));
                }
                else
                {
                    Update();
                    if ((DateTime.Now - stepStart).TotalMilliseconds < _updateTimeout)
                        Thread.Sleep(Math.Max(0, _updateTimeout - (int)(DateTime.Now - stepStart).TotalMilliseconds));
                }
            }
        }

        protected OneConnectionMonitoringNode(string name, string address, MonitoringNodeBase parent = null) : base(name, parent)
        {
            _continue = true;
            _connection = new Connection();
            _lockObject = new object();
            _address = address;

            //Client = new HttpClient(address);

            _nodeThread = new Thread(NodeLoopStep);
            _nodeThread.Name = $"Thread_{name}";
            _nodeThread.Start();
        }

        public void Connect()
        {
            try
            {
                var response = Client.Get();

                lock (_lockObject)
                {
                    _currentInput = response;
                }

                MonitoringNodeUpdate updateObj = ConvertResponse(response);

                
                Update(updateObj, Handler);

                lock (_lockObject)
                {
                    if (_connection.Status != Connection.ConnectionsStatus.Ok)
                    {
                        _connection.Status = Connection.ConnectionsStatus.Ok;
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_lockObject)
                {
                    if (_connection.Status != Connection.ConnectionsStatus.Error)
                    {
                        _connection.Status = Connection.ConnectionsStatus.Error;
                    }

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        Counters.Clear();
                        SubNodes.Clear();
                    });
                    _currentInput = string.Empty;
                }
            }
        }

        public void Update()
        {
            try
            {
                var response = Client.Get();
                MonitoringNodeUpdate updateObj = ConvertResponse(response);
                Update(updateObj, Handler);

                lock (_lockObject)
                {
                    if (_connection.Status != Connection.ConnectionsStatus.Ok)
                    {
                        _connection.Status = Connection.ConnectionsStatus.Ok;
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_lockObject)
                {
                    if (_connection.Status != Connection.ConnectionsStatus.Error)
                    {
                        _connection.Status = Connection.ConnectionsStatus.Error;
                    }
                }
            }
        }

        public abstract MonitoringNodeUpdate ConvertResponse(object responseObj);
    }
}
