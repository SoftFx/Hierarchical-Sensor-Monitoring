using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.Dashboards.Panels.Modules;
using System;

namespace HSMServer.Dashboards
{
    public sealed class PanelSubscription : BasePanelModule<PanelSubscriptionUpdate, PanelSubscriptionEntity>
    {
        public string PathTempalte { get; private set; }


        public PanelSubscription(PanelSubscriptionEntity entity) : base(entity) { }


        protected override void ApplyUpdate(PanelSubscriptionUpdate update)
        {
            if (!string.IsNullOrEmpty(update.PathTemplate))
                PathTempalte = update.PathTemplate;
        }

        public override PanelSubscriptionEntity ToEntity()
        {
            throw new NotImplementedException();
        }
    }
}