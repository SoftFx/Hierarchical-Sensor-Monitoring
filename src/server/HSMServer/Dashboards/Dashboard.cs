using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Dashboards
{
    public sealed class Dashboard : BaseServerModel<DashboardEntity, DashboardUpdate>
    {
        private static readonly TimeSpan _defaultDataPeriod = new(0, 30, 0);
        private Func<Guid, BaseSensorModel> _getSensorModel;


        public ConcurrentDictionary<Guid, Panel> Panels { get; } = new();

        public TimeSpan DataPeriod { get; private set; } = _defaultDataPeriod;


        internal event Action UpdatedEvent;


        internal Dashboard(DashboardAdd addModel) : base(addModel) { }

        internal Dashboard(DashboardEntity entity, Func<Guid, BaseSensorModel> getSensorModel) : base(entity)
        {
            _getSensorModel += getSensorModel;

            Panels = new ConcurrentDictionary<Guid, Panel>(entity.Panels.ToDictionary(k => new Guid(k.Id), AddPanel));
            DataPeriod = GetPeriod(entity.DataPeriod);
        }


        public override void Update(DashboardUpdate update)
        {
            base.Update(update);

            DataPeriod = update.FromPeriod;

            ThrowUpdateEvent();
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
            UpdatedEvent = null;
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

        private void ThrowUpdateEvent() => UpdatedEvent?.Invoke();
    }
}