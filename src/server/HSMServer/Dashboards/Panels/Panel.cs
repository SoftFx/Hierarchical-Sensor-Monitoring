using HSMCommon.Collections;
using HSMCommon.Collections.Reactive;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using System;
using System.Linq;

namespace HSMServer.Dashboards
{
    public sealed class Panel : BaseServerModel<DashboardPanelEntity, PanelUpdate>
    {
        private const int MaxCountOfSources = 100;

        private readonly CDict<PanelSensorScanTask> _scanSensorsTasks = [];
        private readonly CDict<CHash<Guid>> _sensorToSourceMap = [];
        private readonly Dashboard _board;


        public RDict<PanelSubscription> Subscriptions { get; }

        public RDict<PanelDatasource> Sources { get; }


        public PanelSettings Settings { get; } = new();


        public SensorType? MainSensorType { get; private set; }

        public Unit? MainUnit { get; private set; }


        public bool AggregateValues { get; private set; }

        public bool ShowProduct { get; private set; }


        internal Panel(Dashboard board) : base()
        {
            _board = board;

            Subscriptions = new RDict<PanelSubscription>(ThrowUpdateEvent);
            Sources = new RDict<PanelDatasource>(ThrowUpdateEvent);

            AggregateValues = true;
        }

        internal Panel(DashboardPanelEntity entity, Dashboard board) : base(entity)
        {
            _board = board;

            Subscriptions = new RDict<PanelSubscription>(ThrowUpdateEvent);
            Sources = new RDict<PanelDatasource>(ThrowUpdateEvent);

            AggregateValues = !entity.IsNotAggregate;
            ShowProduct = entity.ShowProduct;

            if (entity.Settings is not null)
                Settings.FromEntity(entity.Settings);

            foreach (var sourceEntity in entity.Sources)
                TryAddSource(new Guid(sourceEntity.SensorId), sourceEntity);

            foreach (var subEntity in entity.Subsctiptions)
                TryAddSubscription(new PanelSubscription(subEntity));
        }


        protected override void ApplyUpdate(PanelUpdate update)
        {
            ShowProduct = update.ShowProduct ?? ShowProduct;

            Settings.Update(update);

            if (update.IsAggregateValues.HasValue && update.IsAggregateValues != AggregateValues)
            {
                AggregateValues = update.IsAggregateValues.Value;

                foreach (var (_, source) in Sources)
                    source.BuildSource(AggregateValues);
            }
        }

        public bool TryGetScanTask(Guid templateId, out PanelSensorScanTask task)
        {
            task = null;

            if (!Subscriptions.TryGetValue(templateId, out var sub))
                return false;

            if (!_scanSensorsTasks.TryGetValue(templateId, out task))
                _ = _scanSensorsTasks[templateId].StartScanning(_board.GetSensorsByFolder(null), sub);

            return true;
        }

        public override DashboardPanelEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.Subsctiptions.AddRange(Subscriptions.Select(u => u.Value.ToEntity()));
            entity.Sources.AddRange(Sources.Select(u => u.Value.ToEntity()));

            entity.Settings = Settings.ToEntity();
            entity.IsNotAggregate = !AggregateValues;
            entity.ShowProduct = ShowProduct;

            return entity;
        }

        public override void Dispose()
        {
            base.Dispose();

            foreach ((_, var source) in Sources)
                source.Dispose();

            foreach ((_, var sub) in Subscriptions)
                sub.Dispose();
        }


        public bool TryAddSource(Guid sensorId, PanelSourceEntity entity)
        {
            return _board.TryGetSensor(sensorId, out var sensor) && TrySaveNewSource(new PanelDatasource(sensor, entity), out _).IsOk;
        }

        public bool TryAddSource(Guid sensorId, out PanelDatasource source, out string error)
        {
            source = _board.TryGetSensor(sensorId, out var sensor) ? new PanelDatasource(sensor) : null;

            return TrySaveNewSource(source, out error).ThenCall().IsOk; ;
        }

        public bool TryRemoveSource(Guid sourceId)
        {
            void RemoveSource(PanelDatasource source)
            {
                if (Sources.IsEmpty)
                {
                    MainSensorType = null;
                    MainUnit = null;
                }
            }

            return Sources.IfTryRemoveAndDispose(sourceId).ThenCallForSuccess(RemoveSource).ThenCall().IsOk;
        }

        public void RemoveSensor(Guid sensorId)
        {
            foreach (var sourceId in _sensorToSourceMap[sensorId])
                TryRemoveSource(sourceId);
        }


        public bool TryAddSubscription(PanelSubscription sub) => Subscriptions.IfTryAdd(sub.Id, sub, SubscribeModuleToUpdates).IsOk;

        public bool TryAddSubscription(out PanelSubscription subscription)
        {
            subscription = new PanelSubscription();

            return Subscriptions.TryCallAdd(subscription.Id, subscription, SubscribeModuleToUpdates);
        }

        public bool TryRemoveSubscription(Guid id) => Subscriptions.TryCallRemoveAndDispose(id);


        private RDictResult<PanelDatasource> TrySaveNewSource(PanelDatasource source, out string error)
        {
            var errorResult = RDictResult<PanelDatasource>.ErrorResult;
            error = string.Empty;

            if (source is null)
            {
                error = "Source not found";
                return errorResult;
            }

            var sourceUnit = source.Sensor.OriginalUnit;
            var sourceType = source.Sensor.Type;

            if (!IsSupportedType(sourceType) || !MainSensorType.IsNullOrEqual(sourceType))
                error = $"Can't plot using {sourceType} sensor type";
            else if (!MainUnit.IsNullOrEqual(sourceUnit))
                error = $"Can't plot using {sourceUnit} unit type";
            else if (Sources.Count >= MaxCountOfSources)
                error = $"Max count of sources is {MaxCountOfSources}. Cannot add the new one";

            void ApplyNewSource(PanelDatasource source)
            {
                MainSensorType = sourceType;
                MainUnit = sourceUnit ?? MainUnit;

                _sensorToSourceMap[source.Sensor.Id].Add(source.Id);

                source.BuildSource(AggregateValues);
                SubscribeModuleToUpdates(source);
            }

            return string.IsNullOrEmpty(error) ? Sources.IfTryAdd(source.Id, source, ApplyNewSource) : errorResult;
        }

        private void SubscribeModuleToUpdates(IPanelModule module) => module.UpdateEvent += ThrowUpdateEvent;


        private static bool IsSupportedType(SensorType type) =>
            type is SensorType.Integer or SensorType.Double or SensorType.TimeSpan or SensorType.IntegerBar or SensorType.DoubleBar;
    }
}