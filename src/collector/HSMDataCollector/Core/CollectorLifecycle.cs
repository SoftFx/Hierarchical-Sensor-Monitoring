namespace HSMDataCollector.Core
{
    internal sealed class CollectorLifecycle
    {
        private readonly object _lock = new object();
        private CollectorStatus _status = CollectorStatus.Stopped;
        private bool _disposed;


        internal CollectorStatus Status
        {
            get
            {
                lock (_lock)
                    return _disposed ? CollectorStatus.Disposed : _status;
            }
        }

        internal bool CanAcceptData
        {
            get
            {
                lock (_lock)
                {
                    var s = _status;
                    return s == CollectorStatus.Starting || s == CollectorStatus.Running || s == CollectorStatus.Stopping;
                }
            }
        }

        internal bool CanStartNewSensors
        {
            get
            {
                lock (_lock)
                    return !_disposed && (_status == CollectorStatus.Starting || _status == CollectorStatus.Running);
            }
        }


        internal bool TryStart()
        {
            lock (_lock)
            {
                if (_disposed || _status != CollectorStatus.Stopped)
                    return false;

                _status = CollectorStatus.Starting;
                return true;
            }
        }

        internal bool CompleteStart()
        {
            lock (_lock)
            {
                if (_status != CollectorStatus.Starting)
                    return false;

                _status = CollectorStatus.Running;
                return true;
            }
        }

        internal bool AbortStart()
        {
            lock (_lock)
            {
                if (_status != CollectorStatus.Starting)
                    return false;

                _status = CollectorStatus.Stopped;
                return true;
            }
        }

        internal bool TryStop()
        {
            lock (_lock)
            {
                if (_status != CollectorStatus.Starting && _status != CollectorStatus.Running)
                    return false;

                _status = CollectorStatus.Stopping;
                return true;
            }
        }

        internal bool CompleteStop()
        {
            lock (_lock)
            {
                if (_status != CollectorStatus.Stopping)
                    return false;

                _status = CollectorStatus.Stopped;
                return true;
            }
        }

        /// <returns>The lifecycle status before disposal, or Disposed if already disposed.</returns>
        internal CollectorStatus TryDispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return CollectorStatus.Disposed;

                _disposed = true;
                return _status;
            }
        }
    }
}
