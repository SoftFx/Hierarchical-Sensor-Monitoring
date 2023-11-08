using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using System;
using System.Drawing;

namespace HSMServer.Dashboards
{
    public sealed class PanelDataSource
    {
        public Guid Id { get; }

        public Guid SensorId { get; }


        public Color Color { get; set; }

        public string Label { get; set; }


        public PanelDataSource() { }

        public PanelDataSource(Guid sensorId, Color color, string label = "")
        {
            Id = Guid.NewGuid();
            SensorId = sensorId;
            Color = color;
            Label = label;
        }

        public PanelDataSource(PanelSourceEntity entity)
        {
            Id = new Guid(entity.Id);
            SensorId = new Guid(entity.SensorId);

            Color = Color.FromArgb(entity.Color);

            Label = entity.Label;
        }


        public PanelSourceEntity ToEntity() =>
            new()
            {
                Id = Id.ToByteArray(),
                SensorId = SensorId.ToByteArray(),

                Color = Color.ToArgb(),
                Label = Label,
            };
    }
}