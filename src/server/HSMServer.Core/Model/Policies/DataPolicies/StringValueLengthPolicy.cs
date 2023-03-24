namespace HSMServer.Core.Model.Policies
{
    internal sealed class StringValueLengthPolicy : DataPolicy<StringValue>
    {
        internal const int DefaultMaxStringLength = 150;


        protected override SensorStatus FailStatus => SensorStatus.Warning;

        protected override string FailMessage => "The value has exceeded the length limit.";


        public int MaxStringLength { get; init; } = DefaultMaxStringLength;


        // Parematerless constructor for deserializing policy in PolicyDeserializationConverter
        public StringValueLengthPolicy() { }


        internal override PolicyResult Validate(StringValue value)
        {
            return value.Value?.Length > MaxStringLength ? Fail : Ok;
        }
    }
}