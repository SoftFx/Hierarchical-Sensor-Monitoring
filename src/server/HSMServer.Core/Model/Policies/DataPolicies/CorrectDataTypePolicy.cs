namespace HSMServer.Core.Model.Policies
{
    internal sealed class CorrectDataTypePolicy<T> : Policy where T : BaseValue
    {
        private PolicyResult Fail { get; }

        protected override SensorStatus FailStatus => SensorStatus.Error;

        protected override string FailMessage => $"Sensor value type is not {typeof(T).Name}";


        internal CorrectDataTypePolicy()
        {
            Fail = new(FailStatus, FailMessage, FailIcon);
        }


        internal PolicyResult Validate(T value)
        {
            return value is not null ? Ok : Fail;
        }
    }
}
