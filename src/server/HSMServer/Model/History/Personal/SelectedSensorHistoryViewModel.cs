using HSMCommon.Model;
using HSMServer.Core;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Model.Model.History;
using System.Threading.Tasks;


namespace HSMServer.Model.History
{
    public sealed class SelectedSensorHistoryViewModel
    {
        private GetSensorHistoryRequest _request;
        private BaseSensorModel _sensor;
        private BarBaseValue _lastBar;


        public ChartValuesViewModel Chart { get; private set; }

        public HistoryTableViewModel Table { get; private set; }


        public int NewValuesCnt { get; private set; }


        // One user may have several requests in flight (two browser tabs), and table, export and file
        // requests connect the sensor they name: the switch from one sensor to another is one step, so
        // two of them cannot both unsubscribe the old sensor and leave two subscriptions behind.
        private readonly object _connection = new();

        public void ConnectSensor(BaseSensorModel newSensor)
        {
            lock (_connection)
            {
                if (_sensor?.Id == newSensor.Id)
                    return;

                Unsubscribe(_sensor);
                Subscribe(newSensor);
            }
        }


        public Task Reload(ITreeValuesCache cache, GetSensorHistoryRequest request)
        {
            Reload(request);

            NewValuesCnt = 0;

            return Table.Reload(cache, request);
        }

        public void Reload(GetSensorHistoryRequest request)
        {
            _request = request;
        }


        private void Unsubscribe(BaseSensorModel sensor)
        {
            if (sensor == null)
                return;

            sensor.ReceivedNewValue -= NewSensorValueHandler;

            Table?.Dispose();
        }

        private void Subscribe(BaseSensorModel sensor)
        {
            if (sensor == null)
                return;

            _sensor = sensor;

            if (_sensor.Type.IsBar())
                _lastBar = _sensor.LastValue as BarBaseValue;

            Table = new HistoryTableViewModel(_sensor);
            
            NewValuesCnt = 0;

            sensor.ReceivedNewValue += NewSensorValueHandler;
        }

        private void NewSensorValueHandler(BaseValue value)
        {
            if (_request == null || _request.FromUtc > value.LastUpdateTime || _request.ToUtc < value.ReceivingTime)
                return;

            if (_sensor.Type.IsBar() && value is BarBaseValue barValue)
            {
                if (_lastBar != null && _lastBar.OpenTime == barValue.OpenTime)
                    return;

                _lastBar = barValue;
            }

            NewValuesCnt++;
        }
    }
}