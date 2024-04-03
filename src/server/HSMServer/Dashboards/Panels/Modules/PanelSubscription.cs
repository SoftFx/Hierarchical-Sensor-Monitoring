using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.Core;
using HSMServer.Core.Model;
using HSMServer.Datasources;
using HSMServer.PathTemplates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Dashboards
{
    public sealed class PanelSubscription : BasePlotPanelModule<PanelSubscriptionUpdate, PanelSubscriptionEntity>
    {
        private readonly PathTemplateConverter _pathTemplate = new();
        private PanelRangeSettings _panelYSettings;
        private bool _panelAggrValue;


        public HashSet<Guid> Folders { get; private set; }

        public string PathTempalte { get; private set; }


        public PanelSensorScanTask ScannedTask { get; private set; }

        public bool IsSubscribed { get; private set; }

        public bool IsApplied { get; private set; }


        public bool ScanIsFinished => ScannedTask is not null && ScannedTask.IsFinish;


        public PanelSubscription() : base() { }

        public PanelSubscription(PanelSubscriptionEntity entity) : base(entity)
        {
            PathTempalte = ApplyNewTemplate(entity.PathTemplate);
            Folders = entity.Folders?.ToHashSet();

            IsSubscribed = entity.IsSubscribed;
            IsApplied = entity.IsApplied;
        }


        public override void Update(PanelSubscriptionUpdate update)
        {
            base.Update(update);

            if (update.FoldersFilter is not null)
                Folders = update.FoldersFilter.Folders;

            PathTempalte = ApplyNewTemplate(update.PathTemplate);
            Label = update.Label ?? Label;

            IsSubscribed = update.IsSubscribed ?? IsSubscribed;
        }

        public override PanelSubscriptionEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.PathTemplate = PathTempalte;
            entity.IsSubscribed = IsSubscribed;
            entity.IsApplied = IsApplied;
            entity.Folders = Folders?.ToList();

            return entity;
        }

        public PanelSubscription UpdatePanelSettings(PanelRangeSettings ySetting, bool aggrValues)
        {
            _panelAggrValue = aggrValues;
            _panelYSettings = ySetting;

            return this;
        }


        public bool IsPropertySuitable(BaseSensorModel sensor) => DatasourceFactory.IsSupportedPlotProperty(sensor, Property);

        public bool IsEmaPropertySuitable(BaseSensorModel sensor) =>
             Property switch
             {
                 PlottedProperty.EmaValue or PlottedProperty.EmaMin or PlottedProperty.EmaMean or PlottedProperty.EmaMax or PlottedProperty.EmaCount => sensor.Statistics.HasEma(),
                 _ => true
             };

        public bool IsMatchTemplate(BaseSensorModel sensor) =>
            _pathTemplate.IsMatch(sensor.FullPath) && AreFoldersContain(sensor.Root.FolderId);

        public bool IsMatch(BaseSensorModel sensor) => IsPropertySuitable(sensor) && IsEmaPropertySuitable(sensor) && IsMatchTemplate(sensor);

        public string BuildSensorLabel() => _pathTemplate.BuildStringByTempalte(Label) ?? Label;


        public Task StartScanning(Func<HashSet<Guid>, IEnumerable<BaseSensorModel>> getSensors)
        {
            CancelScanning();

            ScannedTask = new PanelSensorScanTask();

            return ScannedTask.StartScanning(getSensors?.Invoke(Folders), this);
        }

        public void CancelScanning() => ScannedTask?.Cancel();


        public IEnumerable<PanelDatasource> BuildMathedSources()
        {
            if (!ScanIsFinished)
                yield break;

            foreach (var sensor in ScannedTask.MatchedSensors)
                if (TryBuildSource(sensor, out var source))
                    yield return source;

            IsSubscribed = true;
            IsApplied = true;
        }

        public bool TryBuildSource(BaseSensorModel sensor, out PanelDatasource source)
        {
            source = null;

            if (IsMatch(sensor))
            {
                source = new PanelDatasource(sensor);

                source.Update(new PanelSourceUpdate
                {
                    Label = _pathTemplate.BuildStringByTempalte(Label),
                    Property = Property.ToString(),
                    Shape = Shape.ToString(),

                    AggregateValues = _panelAggrValue,
                    YRange = _panelYSettings,
                });

                return true;
            }

            return false;
        }


        private string ApplyNewTemplate(string template)
        {
            if (!string.IsNullOrEmpty(template))
                _pathTemplate.ApplyNewTemplate(template, out _);

            return template;
        }

        internal bool AreFoldersContain(Guid? folder) =>
            Folders is null || (!folder.HasValue && Folders.Contains(Guid.Empty)) || Folders.Contains(folder.Value);
    }
}