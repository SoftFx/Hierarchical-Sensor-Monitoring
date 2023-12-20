using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace HSMServer.Dashboards
{
    public sealed class Dashboard : BaseServerModel<DashboardEntity, DashboardUpdate>
    {
        private static readonly TimeSpan _defaultDataPeriod = new(0, 30, 0);
        private Func<Guid, BaseSensorModel> _getSensorModel;


        public ConcurrentDictionary<Guid, Panel> Panels { get; } = new();

        public TimeSpan DataPeriod { get; private set; } = _defaultDataPeriod;


        internal Dashboard(DashboardAdd addModel) : base(addModel) { }

        internal Dashboard(DashboardEntity entity, Func<Guid, BaseSensorModel> getSensorModel) : base(entity)
        {
            _getSensorModel += getSensorModel;

            Panels = new ConcurrentDictionary<Guid, Panel>(entity.Panels.ToDictionary(k => new Guid(k.Id), AddPanel));
            DataPeriod = GetPeriod(entity.DataPeriod);
        }


        protected override void UpdateCustom(DashboardUpdate update)
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
            Unsubscribe();

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


        internal void Subscribe(Func<Guid, BaseSensorModel> getSensorModel) => _getSensorModel ??= getSensorModel;

        internal void Unsubscribe()
        {
            _getSensorModel = null;
            ClearSubscriptions();
        }

        internal bool TryGetSensor(Guid id, out BaseSensorModel sensor)
        {
            sensor = _getSensorModel?.Invoke(id);

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