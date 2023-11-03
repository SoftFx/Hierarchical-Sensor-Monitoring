using HSMCommon.Collections;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using System;
using System.Linq;

namespace HSMServer.Dashboards
{
    public sealed class Dashboard : BaseServerModel<DashboardEntity, DashboardUpdate>
    {
        public CGuidDict<Panel> Panels { get; }


        internal Dashboard(DashboardEntity entity) : base(entity)
        {
            Panels = new CGuidDict<Panel>(entity.Panels.ToDictionary(k => new Guid(k.Id), v => new Panel(v)));
        }

        internal Dashboard(DashboardAdd addModel) : base(addModel) { }
    }
}