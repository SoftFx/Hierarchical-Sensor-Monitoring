using System;

namespace HSMServer.Core.Model.Policies
{
    internal sealed class CorrectDataTypePolicy<T> : DataPolicy where T : BaseValue
    {
        internal PolicyResult PolicyResult { get; }


        public override SensorStatus Status { get; set; } = SensorStatus.Error;


        internal CorrectDataTypePolicy(Guid sensorId)
        {
            Icon = Status.ToIcon();

            AlertComment = $"Sensor value type is not {typeof(T).Name}";

            SensorResult = new(Status, AlertComment);
            PolicyResult = new PolicyResult(sensorId, this);
        }


        internal static bool Validate(T value) => value is not null;
    }
}
