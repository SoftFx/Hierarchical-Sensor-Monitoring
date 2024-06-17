using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using System.Threading.Tasks;

namespace HSMDataCollector.Sensors
{
    internal sealed class LastValueSensorInstant<T> : SensorInstant<T>, ILastValueSensor<T>
    {
        private SensorStatus _lastStatus;
        private string _lastComment;
        private T _lastValue;


        public LastValueSensorInstant(SensorOptions options, T customDefault) : base(options)
        {
            _lastValue = customDefault;
        }


        internal override Task StopAsync()
        {
            SendValue(_lastValue, _lastStatus, _lastComment);
            return base.StopAsync();
        }


        public override void AddValue(T value, string comment) => AddValue(value, SensorStatus.Ok, comment);

        public override void AddValue(T value) => AddValue(value, string.Empty);

        public override void AddValue(T value, SensorStatus status, string comment)
        {
            _lastComment = comment;
            _lastStatus = status;
            _lastValue = value;
        }
    }
}