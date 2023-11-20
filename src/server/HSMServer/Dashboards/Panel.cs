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

        public PanelSettingsEntity Settings { get; set; }


        internal Panel(Dashboard board) : base()
        {
            _board = board;
            Settings = new PanelSettingsEntity();
        }

        internal Panel(DashboardPanelEntity entity, Dashboard board) : base(entity)
        {
            _board = board;
            Settings = entity.Settings ?? new ();
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

        internal static void Relayout(ConcurrentDictionary<Guid,Panel> panels)
        {
            const int layoutWidth = 3;
            const double width = 0.329D;
            const double height = 0.2D;
            const double translateX = 0.329D;
            const double translateY = 0.23D;
                
            var layoutHeight = 0;
            var counter = 0;

            var layoutTakeSize = panels.Count - panels.Count % layoutWidth;
            foreach (var (_, panel) in panels.Take(layoutTakeSize))
            {
                panel.Settings.Width = width;
                panel.Settings.Height = height;
                panel.Settings.X = translateX * counter;
                panel.Settings.Y = translateY * layoutHeight;

                if (counter == layoutWidth - 1)
                {
                    counter = 0;
                    layoutHeight++;
                }
                else 
                    counter++;
            }

            var lastWidth = 0.99 / (panels.Count - layoutTakeSize);
            foreach (var (_, panel) in panels.TakeLast(panels.Count - layoutTakeSize))
            {
                panel.Settings.Width = lastWidth;
                panel.Settings.Height = height;
                panel.Settings.X = lastWidth * counter;
                panel.Settings.Y = translateY * layoutHeight;

                if (counter == layoutWidth - 1)
                {
                    counter = 0;
                    layoutHeight++;
                }
                else 
                    counter++;
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
            entity.Settings = Settings;
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