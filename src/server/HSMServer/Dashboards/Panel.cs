using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.ConcurrentStorage;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace HSMServer.Dashboards
{
    public sealed class Panel : BaseServerModel<DashboardPanelEntity, PanelUpdate>
    {
        private readonly Dashboard _board;


        public ConcurrentDictionary<Guid, PanelDatasource> Sources { get; } = new();

        public PanelSettings Settings { get; set; } = new();


        internal Panel(Dashboard board) : base()
        {
            _board = board;
        }

        internal Panel(DashboardPanelEntity entity, Dashboard board) : base(entity)
        {
            _board = board;

            if (entity.Settings is not null)
                Settings.FromEntity(entity.Settings);

            foreach (var sourceEntity in entity.Sources)
                TryAddSource(new Guid(sourceEntity.SensorId), sourceEntity);
        }


        protected override void UpdateCustom(PanelUpdate update)
        {
            Settings.Update(update);
        }

        public override DashboardPanelEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.Sources.AddRange(Sources.Select(u => u.Value.ToEntity()));
            entity.Settings = Settings.ToEntity();

            return entity;
        }

        public override void Dispose()
        {
            foreach ((_, var source) in Sources)
                source.Source.Dispose();
        }


        public bool TryAddSource(Guid sensorId, PanelSourceEntity entity)
        {
            if (_board.TryGetSensor(sensorId, out var sensor))
            {
                var source = new PanelDatasource(sensor, entity);

                return Sources.TryAdd(source.Id, source);
            }

            return false;
        }

        public bool TryAddSource(Guid sensorId, out PanelDatasource source)
        {
            source = _board.TryGetSensor(sensorId, out var sensor) ? new PanelDatasource(sensor) : null;

            var result = source is not null && Sources.TryAdd(source.Id, source);

            if (result)
                ThrowUpdateEvent();

            return result;
        }
    }
}