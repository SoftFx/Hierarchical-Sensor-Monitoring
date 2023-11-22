using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        internal static void Relayout(ConcurrentDictionary<Guid,Panel> panels, int layerWidth)
        {
            const double height = 0.2D;
            const double translateY = 0.24D;
            const double currentWidth = 0.984D;
            const double gap = 0.01D;
            var layoutHeight = 0;
            var counter = 0;
            var layoutTakeSize = panels.Count - panels.Count % layerWidth;
            var width = currentWidth - gap * (layerWidth - 1);

            Relayout(panels.Take(layoutTakeSize), width / layerWidth);
            Relayout(panels.TakeLast(panels.Count - layoutTakeSize), (currentWidth - gap * (panels.Count - layoutTakeSize - 1)) / (panels.Count - layoutTakeSize));

            void Relayout(IEnumerable<KeyValuePair<Guid, Panel>> panels, double width)
            {
                foreach (var (_, panel) in panels)
                {
                    panel.Settings.Width = width;
                    panel.Settings.Height = height;
                    panel.Settings.X = (width + gap) * counter;
                    panel.Settings.Y = translateY * layoutHeight;

                    if (counter == layerWidth - 1)
                    {
                        counter = 0;
                        layoutHeight++;
                    }
                    else 
                        counter++;
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