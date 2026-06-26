using System.Threading.Tasks;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.Sensors
{
    internal sealed class LastValueSensorInstant<T> : SensorInstant<T, NoDisplayUnit>, ILastValueSensor<T>
    {
        private SensorStatus _lastStatus;
        private string _lastComment;
        private T _lastValue;

        protected override bool IsLastValue => true;


        public LastValueSensorInstant(InstantSensorOptions options, T customDefault) : base(options)
        {
            SensorValueExtensions.ThrowIfUnsupportedValue(customDefault);
            _lastValue = customDefault;
        }


        public override ValueTask StopAsync()
        {
            SendValue(_lastValue, _lastStatus, _lastComment);
            return base.StopAsync();
        }

        protected override ValueTask DisposeAsyncCore() => base.StopAsync();


        public override void AddValue(T value, string comment) => AddValue(value, SensorStatus.Ok, comment);

        public override void AddValue(T value) => AddValue(value, string.Empty);

        public override void AddValue(T value, SensorStatus status, string comment)
        {
            if (!SensorValueExtensions.IsValidValue(value, status))
            {
                _dataProcessor.LogDroppedValue(SensorPath, $"last-value update failed validation (status: {status})");
                return;
            }

            _lastComment = comment;
            _lastStatus = status;
            _lastValue = value;
        }
    }
}
