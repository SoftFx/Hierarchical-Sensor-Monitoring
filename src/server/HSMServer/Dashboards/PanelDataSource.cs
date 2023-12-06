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
        public SensorDatasourceBase Source { get; }

        public Guid SensorId { get; }

        public Guid Id { get; }


        public Color Color { get; private set; }

        public string Label { get; private set; }


        public PanelDatasource(BaseSensorModel sensor)
        {
            Label = sensor.DisplayName;
            SensorId = sensor.Id;

            Source = DatasourceFactory.Build(sensor.Type).AttachSensor(sensor);
            Color = Color.FromName(ColorExtensions.GenerateRandomColor());
            Id = Guid.NewGuid();
        }

        public PanelDatasource(BaseSensorModel sensor, PanelSourceEntity entity) : this(sensor)
        {
            Id = new Guid(entity.Id);
            SensorId = new Guid(entity.SensorId);

            Color = Color.FromName(entity.Color);
            Label = entity.Label;
        }

        public PanelDatasource Update(PanelSourceUpdate update)
        {
            Color = update.Color is not null ? Color.FromName(update.Color) : Color;
            Label = !string.IsNullOrEmpty(update.Name) ? update.Name : Label;

            return this;
        }


        public PanelSourceEntity ToEntity() =>
            new()
            {
                Id = Id.ToByteArray(),
                SensorId = SensorId.ToByteArray(),

                Color = Color.Name,
                Label = Label,
            };
    }
}