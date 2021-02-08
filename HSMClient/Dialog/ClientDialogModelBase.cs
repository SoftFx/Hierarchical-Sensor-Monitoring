using System;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.ViewModel;

namespace HSMClient.Dialog
{
    public class ClientDialogModelBase : ModelBase, ISensorDialogModel
    {
        protected ISensorHistoryConnector _connector;
        protected string _path;
        protected string _name;
        protected string _product;

        public ClientDialogModelBase(ISensorHistoryConnector connector, MonitoringSensorViewModel sensor)
        {
            _path = sensor.Path;
            _connector = connector;
            _name = sensor.Name;
            _product = sensor.Product;
        }

        #region IDisposable implementation

        // Disposed flag.
        private bool _disposed;

        // Implement IDisposable.
        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposingManagedResources)
        {
            // The idea here is that Dispose(Boolean) knows whether it is 
            // being called to do explicit cleanup (the Boolean is true) 
            // versus being called due to a garbage collection (the Boolean 
            // is false). This distinction is useful because, when being 
            // disposed explicitly, the Dispose(Boolean) method can safely 
            // execute code using reference type fields that refer to other 
            // objects knowing for sure that these other objects have not been 
            // finalized or disposed of yet. When the Boolean is false, 
            // the Dispose(Boolean) method should not execute code that 
            // refer to reference type fields because those objects may 
            // have already been finalized."

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
        }

        // Use C# destructor syntax for finalization code.
        ~ClientDialogModelBase()
        {
            // Simply call Dispose(false).
            Dispose(false);
        }

        #endregion
    }
}
