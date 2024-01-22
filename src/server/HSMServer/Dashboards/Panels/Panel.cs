﻿using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.ConcurrentStorage;
using HSMServer.Core;
using HSMServer.Core.Model;
using HSMServer.Dashboards.Panels.Modules;
using HSMServer.Extensions;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace HSMServer.Dashboards
{
    public sealed class Panel : BaseServerModel<DashboardPanelEntity, PanelUpdate>
    {
        private readonly Dashboard _board;


        public ConcurrentDictionary<Guid, PanelSubscription> Subscriptions { get; } = new();

        public ConcurrentDictionary<Guid, PanelDatasource> Sources { get; } = new();

        public PanelSettings Settings { get; } = new();


        public SensorType? MainSensorType { get; private set; }

        public Unit? MainUnit { get; private set; }


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

            foreach (var subEntity in entity.Subsctiptions)
                TryAddSubscription(new PanelSubscription(subEntity));
        }


        protected override void ApplyUpdate(PanelUpdate update)
        {
            Settings.Update(update);
        }

        public override DashboardPanelEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.Subsctiptions.AddRange(Subscriptions.Select(u => u.Value.ToEntity()));
            entity.Sources.AddRange(Sources.Select(u => u.Value.ToEntity()));

            entity.Settings = Settings.ToEntity();

            return entity;
        }

        public override void Dispose()
        {
            ClearSubscriptions();

            foreach ((_, var source) in Sources)
                source.Dispose();

            foreach ((_, var sub) in Subscriptions)
                sub.Dispose();
        }


        public bool TryAddSource(Guid sensorId, PanelSourceEntity entity)
        {
            return _board.TryGetSensor(sensorId, out var sensor) && TrySaveNewSource(new PanelDatasource(sensor, entity), out _);
        }

        public bool TryAddSource(Guid sensorId, out PanelDatasource source, out string error)
        {
            source = _board.TryGetSensor(sensorId, out var sensor) ? new PanelDatasource(sensor) : null;

            var result = TrySaveNewSource(source, out error);

            if (result)
                ThrowUpdateEvent();

            return result;
        }

        public bool TryRemoveSource(Guid sourceId)
        {
            if (Sources.TryRemove(sourceId, out var source))
            {
                if (Sources.IsEmpty)
                {
                    MainSensorType = null;
                    MainUnit = null;
                }

                UnsubscribeModuleWithCall(source);

                return true;
            }

            return false;
        }


        public bool TryAddSubscription(PanelSubscription subscription)
        {
            var result = Subscriptions.TryAdd(subscription.Id, subscription);

            if (result)
                subscription.UpdateEvent += ThrowUpdateEvent;

            return result;
        }

        public bool TryAddSubscription(out PanelSubscription subscription)
        {
            subscription = new PanelSubscription();

            var result = TryAddSubscription(subscription);

            if (result)
                ThrowUpdateEvent();

            return result;
        }

        public bool TryRemoveSubscription(Guid id)
        {
            var result = Subscriptions.TryRemove(id, out var sub);

            if (result)
                UnsubscribeModuleWithCall(sub);

            return result;
        }


        private bool TrySaveNewSource(PanelDatasource source, out string error)
        {
            bool TryAddNewSource(PanelDatasource source)
            {
                var existingSource = Sources.FirstOrDefault(x => x.Key != source.Id && x.Value.SensorId == source.SensorId).Value;

                return (existingSource is null || existingSource.Sensor.Type.IsBar()) && Sources.TryAdd(source.Id, source);
            }

            error = string.Empty;

            if (source is null)
            {
                error = "Source not found";
                return false;
            }

            var sourceUnit = source.Sensor.OriginalUnit;
            var sourceType = source.Sensor.Type;

            if (!IsSupportedType(sourceType) || !MainSensorType.IsNullOrEqual(sourceType))
                error = $"Can't plot using {sourceType} sensor type";
            else if (!MainUnit.IsNullOrEqual(sourceUnit))
                error = $"Can't plot using {sourceUnit} unit type";
            else if (!TryAddNewSource(source))
                error = "Source already exists";
            else
            {
                MainSensorType = sourceType;
                MainUnit = sourceUnit ?? MainUnit;

                source.BuildSource();
                source.UpdateEvent += ThrowUpdateEvent;
            }

            return string.IsNullOrEmpty(error);
        }


        private void UnsubscribeModuleWithCall(IPanelModule module)
        {
            module.Dispose();
            ThrowUpdateEvent();
        }

        private static bool IsSupportedType(SensorType type) =>
            type is SensorType.Integer or SensorType.Double or SensorType.TimeSpan or SensorType.IntegerBar or SensorType.DoubleBar;
    }
}