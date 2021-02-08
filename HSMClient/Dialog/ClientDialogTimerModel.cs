using System;
using System.Threading;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.ViewModel;

namespace HSMClient.Dialog
{
    public abstract class ClientDialogTimerModel : ClientDialogModelBase
    {
        private Thread _dialogTimerThread;
        private bool _continue;
        private int _interval;
        protected ClientDialogTimerModel(ISensorHistoryConnector connector, MonitoringSensorViewModel sensor,
            int timerInterval = 5000) : base(connector, sensor)
        {
            _interval = timerInterval;
            _continue = true;
            _dialogTimerThread = new Thread(Timer_Loop);
            _dialogTimerThread.Start();
        }

        protected abstract void OnTimerTick();

        private void Timer_Loop()
        {
            while (_continue)
            {
                DateTime start = DateTime.Now;
                OnTimerTick();
                DateTime end = DateTime.Now;
                int diff = (int) ((end - start).TotalMilliseconds);
                if(diff < _interval)
                    Thread.Sleep(diff);
            }
        }

        #region IDisposable implementation

        // Disposed flag.
        private bool _disposed;

        protected override void Dispose(bool disposingManagedResources)
        {
            if (!_disposed)
            {
                if (disposingManagedResources)
                {
                    _continue = false;
                    _dialogTimerThread.Join();
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
    }
}
