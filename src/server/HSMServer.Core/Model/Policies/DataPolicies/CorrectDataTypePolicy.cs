namespace HSMServer.Core.Model.Policies
{
    internal sealed class CorrectDataTypePolicy<T> : Policy where T : BaseValue
    {
        private const SensorStatus FailStatus = SensorStatus.Error;

        private readonly SensorResult _failResult;


        internal CorrectDataTypePolicy()
        {
            _failResult = new(FailStatus, $"Sensor value type is not {typeof(T).Name}");
        }


        internal SensorResult Validate(T value)
        {
            return value is not null ? Ok : _failResult;
        }
    }
}
