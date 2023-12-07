using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.ConcurrentStorage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace HSMServer.Dashboards
{
    public sealed class Panel : BaseServerModel<DashboardPanelEntity, PanelUpdate>
    {
        private readonly Dashboard _board;


        public ConcurrentDictionary<Guid, PanelDatasource> Sources { get; } = new();

        public PanelSettings Settings { get; set; } = new();


        internal event Action UpdatedEvent;


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


        public override void Update(PanelUpdate update)
        {
            base.Update(update);

            Settings.Update(update);

            ThrowUpdateEvent();
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
                UpdatedEvent?.Invoke();

            return result;
        }

        private void ThrowUpdateEvent() => UpdatedEvent?.Invoke();


        internal static void Relayout(ConcurrentDictionary<Guid, Panel> panels, int layerWidth)
        {
            const double height = 0.2D;
            const double translateY = 0.24D;
            const double currentWidth = 1.0D;
            const double gap = 0.01D;
            var layoutHeight = 0;
            var counter = 0;
            var layoutTakeSize = panels.Count - panels.Count % layerWidth;
            var width = currentWidth - gap * (layerWidth + 1);

            var sortedPanels = panels.OrderBy(x => x.Value.Name?.ToLower()).ToList();

            Relayout(sortedPanels.Take(layoutTakeSize), width / layerWidth);
            Relayout(sortedPanels.TakeLast(panels.Count - layoutTakeSize), (currentWidth - gap * (panels.Count - layoutTakeSize + 1)) / (panels.Count - layoutTakeSize));

            void Relayout(IEnumerable<KeyValuePair<Guid, Panel>> panels, double width)
            {
                foreach (var (panelId, panel) in panels)
                {
                    panel.Update(new PanelUpdate(panelId)
                    {
                        Height = height,
                        Width = width,

                        X = width * counter + gap * (counter + 1),
                        Y = translateY * layoutHeight,
                    });

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
    }
}