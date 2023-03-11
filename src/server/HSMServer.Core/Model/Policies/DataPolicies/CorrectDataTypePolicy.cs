﻿namespace HSMServer.Core.Model.Policies
{
    internal sealed class CorrectDataTypePolicy<T> : DataPolicy<T> where T : BaseValue
    {
        protected override SensorStatus FailStatus => SensorStatus.Error;

        protected override string FailMessage => $"Sensor value type is not {typeof(T).Name}";


        public CorrectDataTypePolicy() : base() { }


        internal override ValidationResult Validate(T value)
        {
            return value is not null ? Ok : _validationFail;
        }
    }
}
