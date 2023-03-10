namespace HSMServer.Core.Model.Policies
{
    internal sealed class StringValueLengthPolicy : DataPolicy<StringValue>
    {
        protected override SensorStatus FailStatus => SensorStatus.Warning;

        protected override string FailMessage => "The value has exceeded the length limit.";


        public int MaxStringLength { get; init; } = StringSensorModel.DefaultMaxStringLength;


        // Parematerless constructor for deserializing policy in PolicyDeserializationConverter
        public StringValueLengthPolicy() { }


        internal override ValidationResult Validate(StringValue value)
        {
            return value.Value?.Length > MaxStringLength ? _validationFail : Ok;
        }
    }
}