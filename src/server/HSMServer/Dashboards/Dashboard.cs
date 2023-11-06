using HSMCommon.Collections;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using System;
using System.Linq;

namespace HSMServer.Dashboards
{
    public sealed class Dashboard : BaseServerModel<DashboardEntity, DashboardUpdate>
    {
        public CGuidDict<Panel> Panels { get; } = new();


        internal Dashboard(DashboardEntity entity) : base(entity)
        {
            Panels = new CGuidDict<Panel>(entity.Panels?.ToDictionary(k => new Guid(k.Id), v => new Panel(v)) ?? new());
        }

        internal Dashboard(DashboardAdd addModel) : base(addModel) { }


        public override DashboardEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.Panels?.AddRange(Panels.Select(u => u.Value.ToEntity()));

            return entity;
        }
    }
}