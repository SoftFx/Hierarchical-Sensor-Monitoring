using System.Drawing;

namespace HSMSensorDataObjects
{
    public sealed class EnumOption
    {
        public int Key { get; set; }
        public string Value { get; set; }
        public string Description { get; set; }
        public Color Color { get; set; }


        public EnumOption(int key, string value, string description, Color color)
        {
            Key = key;
            Value = value;
            Description = description;
            Color = color;
        }
    }
}
