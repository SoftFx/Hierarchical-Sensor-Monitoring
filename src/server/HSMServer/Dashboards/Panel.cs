using HSMCommon.Collections;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.ConcurrentStorage;
using System.Linq;

namespace HSMServer.Dashboards
{
    public sealed class Panel : BaseServerModel<DashboardPanelEntity, PanelUpdate>
    {
        public CGuidDict<PanelDataSource> Sources { get; }


        public Panel() { }

        internal Panel(DashboardPanelEntity entity) : base(entity)
        {
            Sources = new CGuidDict<PanelDataSource>(entity.Sources.Select(u => new PanelDataSource(u))
                                                                   .ToDictionary(k => k.Id, v => v));
        }
    }
}