using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;

namespace HSMServer.Dashboards
{
    public sealed class PanelSubscription : BasePlotPanelModule<PanelSubscriptionUpdate, PanelSubscriptionEntity>
    {
        public string PathTempalte { get; private set; }


        public PanelSubscription() : base() { }

        public PanelSubscription(PanelSubscriptionEntity entity) : base(entity) { }


        protected override void Update(PanelSubscriptionUpdate update)
        {
            if (!string.IsNullOrEmpty(update.PathTemplate))
                PathTempalte = update.PathTemplate;
        }

        public override PanelSubscriptionEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.PathTemplate = PathTempalte;

            return entity;
        }
    }
}