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
        private readonly Dashboard _board;

        public SensorDatasourceBase Source { get; }

        public Guid SensorId { get; }

        public Guid Id { get; }


        public Color Color { get; set; }

        public string Label { get; set; }
        
        public TimeSpan DataPeriod { get; set; }


        public PanelDatasource(BaseSensorModel sensor)
        {
            _sensor = sensor;

            Label = _sensor.DisplayName;

            Source = DatasourceFactory.Build(_sensor.Type).AttachSensor(sensor);
            Color = Color.FromName(ColorExtensions.GenerateRandomColor());
            SensorId = _sensor.Id;
            Id = Guid.NewGuid();
        }

        public PanelDatasource(PanelSourceEntity entity, BaseSensorModel sensor, Dashboard dashboard) : this(sensor)
        {
            _sensor = sensor;

            _board = dashboard;
            Id = new Guid(entity.Id);
            SensorId = new Guid(entity.SensorId);

            Color = Color.FromName(entity.Color);
            Label = entity.Label;
        }


        public (DateTime From, DateTime To) GetFromTo() => (DateTime.UtcNow.AddTicks(-_board.DataPeriod.Ticks), DateTime.UtcNow);

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