namespace HSMServer.Core.Model.Policies
{
    internal sealed class CorrectDataTypePolicy<T> : Policy where T : BaseValue
    {
        private const SensorStatus FailStatus = SensorStatus.Error;

       // private readonly SensorResult _fail;


        internal CorrectDataTypePolicy()
        {
            //_fail = new(FailStatus, $"Sensor value type is not {typeof(T).Name}", FailStatus.ToIcon());
        }


        //internal SensorResult Validate(T value)
        //{
        //    return value is not null ? Ok : _fail;
        //}
    }
}
