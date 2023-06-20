using System;

namespace HSMServer.Core.Model.Policies
{
    internal sealed class CorrectTypePolicy<T> : DefaultPolicyBase where T : BaseValue
    {
        private const SensorStatus PolicyStatus = SensorStatus.Error;


        internal CorrectTypePolicy(Guid sensorId) : base(sensorId)
        {
            Status = PolicyStatus;
            Icon = PolicyStatus.ToIcon();

            AlertComment = $"Sensor value type is not {typeof(T).Name}";

            SensorResult = new(Status, AlertComment);
        }


        internal static bool Validate(T value) => value is not null;
    }
}