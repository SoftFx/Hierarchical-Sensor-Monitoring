using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace HSMServer.Dashboards
{
    public sealed class Panel : BaseServerModel<DashboardPanelEntity, PanelUpdate>
    {
        private readonly Dashboard _board;


        public ConcurrentDictionary<Guid, PanelDatasource> Sources { get; } = new();

        public PanelPositionEntity Cords { get; set; }


        internal Panel(Dashboard board) : base()
        {
            _board = board;
            Cords = new PanelPositionEntity();
        }

        internal Panel(DashboardPanelEntity entity, Dashboard board) : base(entity)
        {
            _board = board;
            Cords = entity.Position ?? new ();
            foreach (var sourceEntity in entity.Sources)
            {
                var sensorId = new Guid(sourceEntity.SensorId);

                if (TryGetSensor(sensorId, out var sensor))
                {
                    var panel = new PanelDatasource(sourceEntity, sensor, _board);

                    Sources.TryAdd(panel.Id, panel);
                }
            }
        }


        public bool TryAddSource(Guid sensorId)
        {
            if (TryGetSensor(sensorId, out var sensor))
            {
                var source = new PanelDatasource(sensor, _board);

                return Sources.TryAdd(source.Id, source);
            }

            return false;
        }

        public override DashboardPanelEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.Sources.AddRange(Sources.Select(u => u.Value.ToEntity()));
            entity.Position = Cords;
            return entity;
        }


        private bool TryGetSensor(Guid id, out BaseSensorModel sensor)
        {
            sensor = _board.GetSensorModel?.Invoke(id);

            return sensor is not null;
        }

        public override void Dispose()
        {
            foreach ((_, var source) in Sources)
                source.Source.Dispose();
        }
    }
}