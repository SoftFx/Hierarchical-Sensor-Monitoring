using HSMClientWPFControls.Objects;

namespace HSMClient.ConnectionNode
{
    class ConnectionMonitoringNodeBase : MonitoringNodeBase
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

       

        public ConnectionMonitoringNodeBase(string name, MonitoringNodeBase parent = null) : base(name, parent)
        {
            
        }

        
    }
}
