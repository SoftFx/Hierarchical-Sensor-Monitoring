using System;

namespace HSMServer.Core.Model.Policies
{
    internal sealed class CorrectDataTypePolicy<T> : DataPolicy where T : BaseValue
    {
        private const SensorStatus PolicyStatus = SensorStatus.Error;


        internal PolicyResult PolicyResult { get; }


        internal CorrectDataTypePolicy(Guid sensorId)
        {
            Status = PolicyStatus;
            Icon = PolicyStatus.ToIcon();

            AlertComment = $"Sensor value type is not {typeof(T).Name}";

            SensorResult = new(Status, AlertComment);
            PolicyResult = new PolicyResult(sensorId, this);
        }


        internal static bool Validate(T value) => value is not null;
    }
}