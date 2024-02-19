using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.Core;
using HSMServer.Core.Model;
using HSMServer.Datasources;
using System;

namespace HSMServer.Dashboards
{
    public sealed class PanelDatasource : BasePlotPanelModule<PanelSourceUpdate, PanelSourceEntity>
    {
        public PanelSettings Settings { get; }
        
        public BaseSensorModel Sensor { get; }

        public Guid SensorId { get; }


        public SensorDatasourceBase Source { get; private set; }


        public PanelDatasource(BaseSensorModel sensor, PanelSettings settings = null) : base()
        {
            SensorId = sensor.Id;
            Sensor = sensor;

            Property = sensor.Type.IsBar() ? PlottedProperty.Max : PlottedProperty.Value;
            Label = $"{sensor.DisplayName} ({Property})";
            Settings = settings;
        }

        public PanelDatasource(BaseSensorModel sensor, PanelSourceEntity entity, PanelSettings settings) : base(entity)
        {
            SensorId = new Guid(entity.SensorId);
            Sensor = sensor;
            Settings = settings;
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

            Source = DatasourceFactory.Build(Sensor, settings, Settings);

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

        protected override void ChangeDependentProperties(PanelSourceUpdate update) => BuildSource(update.AggregateValues);
    }
}