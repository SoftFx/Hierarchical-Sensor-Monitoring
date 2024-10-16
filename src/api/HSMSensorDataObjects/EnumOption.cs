using System.Drawing;

namespace HSMSensorDataObjects
{
    public sealed class EnumOption
    {
        public int Key { get; set; }
        public string Value { get; set; }
        public string Description { get; set; }
        public int Color { get; set; }

        public EnumOption() { }

        public EnumOption(int key, string value, string description, Color color) : this(key, value, description, color.ToArgb()) { }

        public EnumOption(int key, string value, string description, int color)
        {
            Key = key;
            Value = value;
            Description = description;
            Color = color;
        }

    }
}
