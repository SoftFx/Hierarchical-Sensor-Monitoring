using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.Core.Model;
using HSMServer.Datasources;
using HSMServer.PathTemplates;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace HSMServer.Dashboards
{
    public sealed class PanelSubscription : BasePlotPanelModule<PanelSubscriptionUpdate, PanelSubscriptionEntity>
    {
        private readonly PathTemplateConverter _pathConverter = new();

        public List<Guid> Folders { get; private set; }

        public string PathTempalte { get; private set; }


        public PanelSensorScanTask ScannedTask { get; private set; }

        public bool IsApplied { get; private set; }


        public PanelSubscription() : base() { }

        public PanelSubscription(PanelSubscriptionEntity entity) : base(entity)
        {
            PathTempalte = ApplyNewTemplate(entity.PathTemplate);
            Folders = entity.Folders;
            IsApplied = entity.IsApplied;
        }


        public override void Update(PanelSubscriptionUpdate update)
        {
            base.Update(update);

            PathTempalte = ApplyNewTemplate(update.PathTemplate);
            Label = update.Label ?? Label;
            Folders = update.Folders;
        }

        public override PanelSubscriptionEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.PathTemplate = PathTempalte;
            entity.IsApplied = IsApplied;
            entity.Folders = Folders;

            return entity;
        }


        public bool IsMatch(BaseSensorModel sensor) => _pathConverter.IsMatch(sensor.Path) && DatasourceFactory.IsSupportedPlotProperty(sensor, Property);

        public string BuildSensorLabel() => _pathConverter.BuildStringByTempalte(Label) ?? Label;


        public Task StartScanning(Func<List<Guid>, IEnumerable<BaseSensorModel>> getSensors)
        {
            CancelScanning();

            ScannedTask = new PanelSensorScanTask();

            return ScannedTask.StartScanning(getSensors?.Invoke(Folders), this);
        }

        public void CancelScanning() => ScannedTask?.Cancel();

        public void Apply()
        {
            IsApplied = true;
        }

        public bool TryBuildSource(BaseSensorModel sensor, out PanelDatasource source)
        {
            source = null;

            if (!IsMatch(sensor))
                return false;

            source = new PanelDatasource(sensor);

            source.Update(new PanelSourceUpdate
            {
                Label = _pathConverter.BuildStringByTempalte(Label),
                Property = nameof(Property),
                Shape = nameof(Shape),
            });

            return true;
        }


        private string ApplyNewTemplate(string template)
        {
            if (!string.IsNullOrEmpty(template))
                _pathConverter.ApplyNewTemplate(template, out _);

            return template;
        }
    }
}