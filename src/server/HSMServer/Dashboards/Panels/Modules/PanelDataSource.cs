using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.Core;
using HSMServer.Core.Model;
using HSMServer.Datasources;
using System;
using System.Drawing;

namespace HSMServer.Dashboards
{
    public sealed class PanelDatasource : BasePlotPanelModule<PanelSourceUpdate, PanelSourceEntity>
    {
        public BaseSensorModel Sensor { get; }

        public Guid SensorId { get; }


        public SensorDatasourceBase Source { get; private set; }


        public PanelDatasource(BaseSensorModel sensor) : base()
        {
            SensorId = sensor.Id;
            Sensor = sensor;

            Property = sensor.Type.IsBar() ? PlottedProperty.Max : PlottedProperty.Value;
            Label = $"{sensor.DisplayName} ({Property})";
        }

        public PanelDatasource(BaseSensorModel sensor, PanelSourceEntity entity) : base(entity)
        {
            SensorId = new Guid(entity.SensorId);
            Sensor = sensor;
        }


        protected override void Update(PanelSourceUpdate update)
        {
            var rebuildSource = false;

            T ApplyRebuild<T>(T value)
            {
                rebuildSource = true;
                return value;
            }

            Color = update.Color is not null ? Color.FromName(update.Color) : Color;
            Label = !string.IsNullOrEmpty(update.Name) ? update.Name : Label;

            if (Enum.TryParse<PlottedProperty>(update.Property, out var newProperty) && Property != newProperty)
                Property = ApplyRebuild(newProperty);

            if (Enum.TryParse<PlottedShape>(update.Shape, out var newShape) && Shape != newShape)
                Shape = newShape;

            if (rebuildSource)
                BuildSource(update.AggregateValues);
        }

        public PanelDatasource BuildSource(bool aggregateValues)
        {
            Source?.Dispose(); // unsubscribe prev version

            var settings = new SourceSettings
            {
                SensorType = Sensor.Type,
                Property = Property,

                AggregateValues = aggregateValues,
            };

            Source = DatasourceFactory.Build(Sensor, settings);

            return this;
        }


        public override PanelSourceEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.SensorId = SensorId.ToByteArray();

            return entity;
        }

        public override void Dispose()
        {
            base.Dispose();

            Source?.Dispose();
        }
    }
}