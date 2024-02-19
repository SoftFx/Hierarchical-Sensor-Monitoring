using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.Core.Model;
using HSMServer.PathTemplates;
using System;
using System.Collections.Generic;

namespace HSMServer.Dashboards
{
    public sealed class PanelSubscription : BasePlotPanelModule<PanelSubscriptionUpdate, PanelSubscriptionEntity>
    {
        private readonly PathTemplateConverter _pathConverter = new();


        public string PathTempalte { get; private set; }

        public List<Guid> Folders { get; private set; }

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

        public bool IsMatch(string path) => _pathConverter.IsMatch(path);

        public void Apply()
        {
            IsApplied = true;

        }

        public bool TryBuildSource(BaseSensorModel sensor, out PanelDatasource source)
        {
            source = null;

            if (!IsMatch(sensor.FullPath))
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