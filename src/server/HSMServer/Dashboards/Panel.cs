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


        internal Panel(DashboardPanelEntity entity, Dashboard board) : base(entity)
        {
            _board = board;

            foreach (var sourceEntity in entity.Sources)
            {
                var sensorId = new Guid(sourceEntity.SensorId);

                if (TryGetSensor(sensorId, out var sensor))
                {
                    var panel = new PanelDatasource(sourceEntity, sensor);

                    Sources.TryAdd(panel.Id, panel);
                }
            }
        }


        public bool TryAddSource(Guid sensorId)
        {
            if (TryGetSensor(sensorId, out var sensor))
            {
                var source = new PanelDatasource(sensor);

                return Sources.TryAdd(source.Id, source);
            }

            return false;
        }

        public override DashboardPanelEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.Sources.AddRange(Sources.Select(u => u.Value.ToEntity()));

            return entity;
        }


        private bool TryGetSensor(Guid id, out BaseSensorModel sensor)
        {
            sensor = _board.GetSensorModel?.Invoke(id);

            return sensor is not null;
        }
    }
}