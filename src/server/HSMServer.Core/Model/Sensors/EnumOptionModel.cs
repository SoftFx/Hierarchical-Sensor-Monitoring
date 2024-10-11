using System.Drawing;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;


namespace HSMServer.Core.Model.Sensors
{
    public class EnumOptionModel
    {
        public string Value { get; set; }
        public string Description { get; set; }
        public int Color { get; set; }

        public EnumOptionModel(EnumOptionEntity entity)
        {
            Value = entity.Value;
            Description = entity.Description;
            Color = entity.Color;
        }

        public EnumOptionModel(EnumOption entity)
        {
            Value = entity.Value;
            Description = entity.Description;
            Color = entity.Color;
        }

        public EnumOptionEntity ToEntity() => new EnumOptionEntity
        {
            Value = Value,
            Description = Description,
            Color = Color
        };
    }
}
