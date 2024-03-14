﻿using HSMCommon.Collections.Reactive;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using System;
using System.Collections.Generic;
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

            Panels = new RDict<Panel>(ThrowUpdateEvent);
            DataPeriod = GetPeriod(entity.DataPeriod);

            foreach (var panelEntity in entity.Panels)
                AddPanel(panelEntity);
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

        internal IEnumerable<BaseSensorModel> GetSensorsByFolder(HashSet<Guid> foldersIds) => _cache.GetSensorsByFolder(foldersIds);


        private bool AddPanel(DashboardPanelEntity entity)
        {
            var panel = new Panel(entity, this);

            return Panels.IfTryAdd(panel.Id, panel, SubscribeToPanelUpdates).IsOk;
        }

        private void SubscribeToPanelUpdates(Panel panel)
        {
            if (panel is not null)
                panel.UpdatedEvent += ThrowUpdateEvent;
        }

        private void ChangeSensorHandler(BaseSensorModel sensor, ActionType action)
        {
            if (action == ActionType.Delete)
            {
                foreach (var (_, panel) in Panels)
                    panel.RemoveSensor(sensor.Id);
            }

            if (action == ActionType.Add)
            {
                foreach (var (_, panel) in Panels)
                    panel.AddSensor(sensor);
            }
        }


        private static TimeSpan GetPeriod(TimeSpan entityPeriod) => entityPeriod == TimeSpan.Zero ? _defaultDataPeriod : entityPeriod;
    }
}