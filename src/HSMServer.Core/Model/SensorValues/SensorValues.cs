namespace HSMServer.Core.Model
{
    public record BooleanValue : BaseValue<bool> { }


    public record IntegerValue : BaseValue<int> { }


    public record DoubleValue : BaseValue<double> { }


    public record StringValue : BaseValue<string> { }


    public record FileValue : BaseValue<byte[]>
    {
        public string Name { get; init; }

        public string Extension { get; init; }

        public long OriginalSize { get; init; }
    }


    public record IntegerBarValue : BarBaseValue<int> { }


    public record DoubleBarValue : BarBaseValue<double> { }
}
