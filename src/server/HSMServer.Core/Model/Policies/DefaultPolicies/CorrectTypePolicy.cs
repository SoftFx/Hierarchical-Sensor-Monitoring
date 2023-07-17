using System;

namespace HSMServer.Core.Model.Policies
{
    internal sealed class CorrectTypePolicy<T> : DefaultPolicyBase where T : BaseValue
    {
        private const SensorStatus PolicyStatus = SensorStatus.Error;


        protected internal override string AlertComment { get; protected set; } = $"Sensor value type is not {typeof(T).Name}";


        public override SensorStatus Status { get; protected set; } = PolicyStatus;

        public override string Icon { get; protected set; } = PolicyStatus.ToIcon();


        internal CorrectTypePolicy(Guid sensorId) : base(sensorId)
        {
            SensorResult = new(Status, AlertComment);
        }


        internal static bool Validate(T value) => value is not null;
    }
}