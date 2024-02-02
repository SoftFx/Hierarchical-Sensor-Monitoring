using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace HSMServer.Dashboards
{
    public sealed class Dashboard : BaseServerModel<DashboardEntity, DashboardUpdate>
    {
        private static readonly TimeSpan _defaultDataPeriod = new(0, 30, 0);
        private readonly ITreeValuesCache _cache;


        public ConcurrentDictionary<Guid, Panel> Panels { get; } = new();

        public TimeSpan DataPeriod { get; private set; } = _defaultDataPeriod;


        internal Dashboard(DashboardAdd addModel, ITreeValuesCache cache) : base(addModel)
        {
            _cache = cache;
        }

        internal Dashboard(DashboardEntity entity, ITreeValuesCache cache) : base(entity)
        {
            _cache = cache;

            Panels = new ConcurrentDictionary<Guid, Panel>(entity.Panels.ToDictionary(k => new Guid(k.Id), AddPanel));
            DataPeriod = GetPeriod(entity.DataPeriod);
        }


        protected override void ApplyUpdate(DashboardUpdate update)
        {
            DataPeriod = update.FromPeriod;
        }

        public override DashboardEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.Panels.AddRange(Panels.Select(u => u.Value.ToEntity()));
            entity.DataPeriod = DataPeriod;

            return entity;
        }

        public override void Dispose()
        {
            base.Dispose();

            foreach (var (_, panel) in Panels)
                panel.Dispose();
        }


        public bool TryAddPanel(Panel panel)
        {
            var result = TrySaveAndSubscribePanel(panel);

            if (result)
                ThrowUpdateEvent();

            return result;
        }

        public bool TryRemovePanel(Guid id)
        {
            var result = Panels.TryRemove(id, out var panel);

            if (result)
            {
                panel.Dispose();
                ThrowUpdateEvent();
            }

            return result;
        }

        public bool AutofitPanels(int panelsInRow)
        {
            var ok = PanelsLayout.RecalculatePanelSize(Panels, panelsInRow);

            if (ok)
                ThrowUpdateEvent();

            return ok;
        }

        internal bool TryGetSensor(Guid id, out BaseSensorModel sensor)
        {
            sensor = _cache.GetSensor(id);

            return sensor is not null;
        }


        private Panel AddPanel(DashboardPanelEntity entity)
        {
            var panel = new Panel(entity, this);

            TrySaveAndSubscribePanel(panel);

            return panel;
        }

        private bool TrySaveAndSubscribePanel(Panel panel)
        {
            var result = panel is not null && Panels.TryAdd(panel.Id, panel);

            if (result)
                panel.UpdatedEvent += ThrowUpdateEvent;

            return result;
        }


        private static TimeSpan GetPeriod(TimeSpan entityPeriod) => entityPeriod == TimeSpan.Zero ? _defaultDataPeriod : entityPeriod;
    }
}