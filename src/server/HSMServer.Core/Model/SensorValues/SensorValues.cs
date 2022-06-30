using HSMServer.Core.DataLayer;
using System;
using System.Text.Json;

namespace HSMServer.Core.Model
{
    public record BooleanValue : BaseValue<bool>
    {
        protected override string ValuePropertyName { get; } = "BoolValue";

        protected override Func<JsonElement, bool> GetValuePropertyAction { get; } = (el) => el.GetBoolean();


        protected BooleanValue(JsonElement element) : base(element) { }

        public BooleanValue() : base() { }


        public override BaseValue BuildValue(JsonElement element) =>
            new BooleanValue(element);
    }


    public record IntegerValue : BaseValue<int>
    {
        protected override string ValuePropertyName { get; } = "IntValue";

        protected override Func<JsonElement, int> GetValuePropertyAction { get; } = (el) => el.GetInt32();


        protected IntegerValue(JsonElement element) : base(element) { }

        public IntegerValue() : base() { }


        public override BaseValue BuildValue(JsonElement element) =>
            new IntegerValue(element);
    }


    public record DoubleValue : BaseValue<double>
    {
        protected override string ValuePropertyName { get; } = "DoubleValue";

        protected override Func<JsonElement, double> GetValuePropertyAction { get; } = (el) => el.GetDouble();


        protected DoubleValue(JsonElement element) : base(element) { }

        public DoubleValue() : base() { }


        public override BaseValue BuildValue(JsonElement element) =>
            new DoubleValue(element);
    }


    public record StringValue : BaseValue<string>
    {
        protected override string ValuePropertyName { get; } = "StringValue";

        protected override Func<JsonElement, string> GetValuePropertyAction { get; } = (el) => el.GetString();


        protected StringValue(JsonElement element) : base(element) { }

        public StringValue() : base() { }


        public override BaseValue BuildValue(JsonElement element) =>
            new StringValue(element);
    }


    public record FileValue : BaseValue<byte[]>
    {
        private const string NamePropertyName = "FileName";
        private const string ExtensionPropertyName = "Extension";
        private const string OriginalSizePropertyName = "OriginalFileSensorContentSize";


        protected override string ValuePropertyName { get; } = "FileContent";

        protected override Func<JsonElement, byte[]> GetValuePropertyAction { get; } = (el) => el.GetBytesFromBase64();


        public string Name { get; init; }

        public string Extension { get; init; }

        public long OriginalSize { get; init; }


        protected FileValue(JsonElement element) : base(element)
        {
            Name = EntityConverter.GetProperty(GetTypedData(element), NamePropertyName, element => element.GetString());
            Extension = EntityConverter.GetProperty(GetTypedData(element), ExtensionPropertyName, element => element.GetString());
            OriginalSize = EntityConverter.GetProperty(element, OriginalSizePropertyName, element => element.GetInt64());
        }

        public FileValue() : base() { }


        public override BaseValue BuildValue(JsonElement element) =>
            new FileValue(element);
    }


    public record IntegerBarValue : BarBaseValue<int>
    {
        protected override Func<JsonElement, int> GetValuePropertyAction { get; } = (el) => el.GetInt32();


        protected IntegerBarValue(JsonElement element) : base(element) { }

        public IntegerBarValue() : base() { }


        public override BaseValue BuildValue(JsonElement element) =>
            new IntegerBarValue(element);
    }


    public record DoubleBarValue : BarBaseValue<double>
    {
        protected override Func<JsonElement, double> GetValuePropertyAction { get; } = (el) => el.GetDouble();


        protected DoubleBarValue(JsonElement element) : base(element) { }

        public DoubleBarValue() : base() { }


        public override BaseValue BuildValue(JsonElement element) =>
            new DoubleBarValue(element);
    }
}
