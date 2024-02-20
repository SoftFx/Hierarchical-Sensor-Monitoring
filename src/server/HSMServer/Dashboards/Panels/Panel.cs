using HSMCommon.Collections;
using HSMCommon.Collections.Reactive;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using System;
using System.Linq;
using System.Text;

namespace HSMServer.Dashboards
{
    public sealed class Panel : BaseServerModel<DashboardPanelEntity, PanelUpdate>
    {
        private const int MaxCountOfSources = 100;

        private readonly CDict<CHash<Guid>> _sensorToSourceMap = [];
        private readonly Dashboard _board;


        public RDict<PanelSubscription> Subscriptions { get; }

        public RDict<PanelDatasource> Sources { get; }


        public PanelRangeSettings YRange { get; } = new();

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

            YRange.FromEntity(entity.YRangeSettings);

            foreach (var sourceEntity in entity.Sources)
                TryAddSource(new Guid(sourceEntity.SensorId), sourceEntity);

            foreach (var subEntity in entity.Subscriptions)
                TryAddSubscription(new PanelSubscription(subEntity));
        }


        protected override void ApplyUpdate(PanelUpdate update)
        {
            AggregateValues = update.IsAggregateValues ?? AggregateValues;
            ShowProduct = update.ShowProduct ?? ShowProduct;

            YRange.Update(update);
            Settings.Update(update);

            if (update.NeedSourceRebuild)
            {
                foreach (var (_, source) in Sources)
                    source.BuildSource(AggregateValues, YRange);
            }
        }

        public override DashboardPanelEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.Subscriptions.AddRange(Subscriptions.Select(u => u.Value.ToEntity()));
            entity.Sources.AddRange(Sources.Select(u => u.Value.ToEntity()));

            entity.YRangeSettings = YRange.ToEntity();
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


        public bool TryStartScan(Guid templateId)
        {
            var result = Subscriptions.TryGetValue(templateId, out var sub);

            if (result)
                _ = sub.StartScanning(_board.GetSensorsByFolder);

            return result;
        }

        public bool TryApplyScanResults(Guid templateId, out string error)
        {
            error = string.Empty;

            if (!Subscriptions.TryGetValue(templateId, out var template) || !template.ScanIsFinished)
                return false;

            var errorBuilder = new StringBuilder(1 << 4);

            foreach (var source in template.BuildMathedSources())
                if (!TrySaveNewSource(source, out var errorBuild).IsOk)
                    errorBuilder.AppendLine(errorBuild);

            error = errorBuilder.ToString();
            Sources.Call();

            return true;
        }


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

                source.BuildSource(AggregateValues, YRange);
                SubscribeModuleToUpdates(source);
            }

            return string.IsNullOrEmpty(error) ? Sources.IfTryAdd(source.Id, source, ApplyNewSource) : errorResult;
        }

        private void SubscribeModuleToUpdates(IPanelModule module) => module.UpdateEvent += ThrowUpdateEvent;


        private static bool IsSupportedType(SensorType type) =>
            type is SensorType.Integer or SensorType.Double or SensorType.TimeSpan or SensorType.IntegerBar or SensorType.DoubleBar;
    }
}