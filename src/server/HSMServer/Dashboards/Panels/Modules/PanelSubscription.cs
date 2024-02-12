using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.Core.Model;
using HSMServer.PathTemplates;

namespace HSMServer.Dashboards
{
    public sealed class PanelSubscription : BasePlotPanelModule<PanelSubscriptionUpdate, PanelSubscriptionEntity>
    {
        private readonly PathTemplateConverter _pathConverter = new();


        public string PathTempalte { get; private set; }


        public PanelSubscription() : base() { }

        public PanelSubscription(PanelSubscriptionEntity entity) : base(entity)
        {
            PathTempalte = entity.PathTemplate;
        }


        public override PanelSubscriptionEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.PathTemplate = PathTempalte;

            return entity;
        }


        public override void Update(PanelSubscriptionUpdate update)
        {
            base.Update(update);

            Label = update.Label ?? Label;
            ApplyNewTemplate(update.PathTemplate);
        }

        public bool TryBuildSource(BaseSensorModel sensor, out PanelDatasource source)
        {
            source = null;

            if (!_pathConverter.IsMatch(sensor.FullPath))
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


        private void ApplyNewTemplate(string template)
        {
            if (!string.IsNullOrEmpty(template) && _pathConverter.ApplyNewTemplate(template, out _))
                PathTempalte = template;
        }
    }
}