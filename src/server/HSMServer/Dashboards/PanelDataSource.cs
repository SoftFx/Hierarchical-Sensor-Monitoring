using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.Core.Model;
using System;
using System.Drawing;

namespace HSMServer.Dashboards
{
    public sealed class PanelDataSource
    {
        private readonly BaseSensorModel _sensor;


        public Guid Id { get; }

        public Guid SensorId { get; }


        public Color Color { get; private set; }

        public string Label { get; private set; }


        public PanelDataSource(BaseSensorModel sensor) 
        {
            _sensor = sensor;

            Id = Guid.NewGuid();
            Label = _sensor.DisplayName;
        }

        public PanelDataSource(PanelSourceEntity entity, BaseSensorModel sensor) : this(sensor)
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