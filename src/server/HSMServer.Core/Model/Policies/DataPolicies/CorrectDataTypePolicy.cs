namespace HSMServer.Core.Model.Policies
{
    internal sealed class CorrectDataTypePolicy<T> : Policy where T : BaseValue
    {
        protected override SensorStatus FailStatus => SensorStatus.Error;

        protected override string FailMessage => $"Sensor value type is not {typeof(T).Name}";


        internal PolicyResult Validate(T value)
        {
            return value is not null ? Ok : Fail;
        }
    }
}
