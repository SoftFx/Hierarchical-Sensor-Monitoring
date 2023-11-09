using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.Core.Model;
using HSMServer.Datasources;
using HSMServer.Extensions;
using System;
using System.Drawing;

namespace HSMServer.Dashboards
{
    public sealed class PanelDatasource
    {
        private readonly BaseSensorModel _sensor;


        public SensorDatasourceBase Source { get; }

        public Guid SensorId { get; }

        public Guid Id { get; }


        public Color Color { get; set; }

        public string Label { get; set; }


        public PanelDatasource(BaseSensorModel sensor)
        {
            _sensor = sensor;

            Label = _sensor.DisplayName;

            Source = DatasourceFactory.Build(_sensor.Type).AttachSensor(sensor);
            Color = Color.FromName(ColorExtensions.GenerateRandomColor());
            SensorId = sensor.Id;
            Id = Guid.NewGuid();
        }

        public PanelDatasource(PanelSourceEntity entity, BaseSensorModel sensor) : this(sensor)
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