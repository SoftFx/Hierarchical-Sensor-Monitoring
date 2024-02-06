using HSMCommon.Collections.Reactive;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using System;
using System.Linq;

namespace HSMServer.Dashboards
{
    public sealed class Dashboard : BaseServerModel<DashboardEntity, DashboardUpdate>
    {
        private static readonly TimeSpan _defaultDataPeriod = new(0, 30, 0);
        private readonly ITreeValuesCache _cache;


        public RDict<Panel> Panels { get; }

        public TimeSpan DataPeriod { get; private set; } = _defaultDataPeriod;


        internal Dashboard(DashboardAdd addModel, ITreeValuesCache cache) : base(addModel)
        {
            _cache = cache;
            _cache.ChangeSensorEvent += ChangeSensorHandler;

            Panels = new RDict<Panel>(ThrowUpdateEvent);
        }

        internal Dashboard(DashboardEntity entity, ITreeValuesCache cache) : base(entity)
        {
            _cache = cache;
            _cache.ChangeSensorEvent += ChangeSensorHandler;

            Panels = new RDict<Panel>(entity.Panels.ToDictionary(k => new Guid(k.Id), AddPanel), ThrowUpdateEvent);
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
            _cache.ChangeSensorEvent -= ChangeSensorHandler;

            base.Dispose();

            foreach (var (_, panel) in Panels)
                panel.Dispose();
        }


        public bool TryAddPanel(Panel panel) => Panels.TryCallAdd(panel.Id, panel, SubscribeToPanelUpdates);

        public bool TryRemovePanel(Guid id) => Panels.TryCallRemoveAndDispose(id);

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

            return Panels.IfTryAdd(panel.Id, panel).ThenCallForSuccess(SubscribeToPanelUpdates).Value;
        }

        private void SubscribeToPanelUpdates(Panel panel)
        {
            if (panel is not null)
                panel.UpdatedEvent += ThrowUpdateEvent;
        }

        private void ChangeSensorHandler(BaseSensorModel model, ActionType action)
        {
            if (action == ActionType.Delete)
            {
                foreach (var (_, panel) in Panels)
                    panel.RemoveSensor(model.Id);
            }
        }


        private static TimeSpan GetPeriod(TimeSpan entityPeriod) => entityPeriod == TimeSpan.Zero ? _defaultDataPeriod : entityPeriod;
    }
}