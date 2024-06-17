using HSMDataCollector.Options;
using System;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors.Windows.Service
{
    internal sealed class WindowsServiceStatusSensor : SensorBase<int>
    {
        private readonly TimeSpan _scanPeriod = TimeSpan.FromSeconds(5);
        private readonly ServiceController _controller;

        private ServiceControllerStatus _lastServiceState;
        private Timer _statusWatcher;


        internal WindowsServiceStatusSensor(ServiceSensorOptions options) : base(options)
        {
            _controller = GetService(options.ServiceName);
            _lastServiceState = _controller.Status;
        }


        internal override Task<bool> StartAsync()
        {
            if (_statusWatcher == null)
                _statusWatcher = new Timer(CheckServiceStatus, null, _scanPeriod, _scanPeriod);

            return base.StartAsync();
        }

        internal override Task StopAsync()
        {
            _statusWatcher?.Dispose();
            _controller?.Dispose();

            return base.StopAsync();
        }


        private void CheckServiceStatus(object _)
        {
            try
            {
                _controller.Refresh();

                if (_controller.Status != _lastServiceState)
                {
                    _lastServiceState = _controller.Status;
                    SendValue((int)_lastServiceState);
                }
            }
            catch (Exception ex)
            {
                ThrowException(ex);
            }
        }

        private ServiceController GetService(string serviceName) =>
            ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == serviceName) ??
            throw new ArgumentException($"Service {serviceName} not found!");
    }
}
