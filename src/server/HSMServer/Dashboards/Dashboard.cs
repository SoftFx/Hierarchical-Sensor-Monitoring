using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace HSMServer.Dashboards
{
    public sealed class Dashboard : BaseServerModel<DashboardEntity, DashboardUpdate>
    {
        public ConcurrentDictionary<Guid, Panel> Panels { get; } = new();


        public DateTime FromDataPeriod { get; private set; }

        public DateTime? ToDataPeriod { get; private set; }


        internal Func<Guid, BaseSensorModel> GetSensorModel;


        internal Dashboard(DashboardEntity entity) : base(entity)
        {
            Panels = new ConcurrentDictionary<Guid, Panel>(entity.Panels.ToDictionary(k => new Guid(k.Id), v => new Panel(v, this)));
        }

        internal Dashboard(DashboardAdd addModel) : base(addModel) { }


        public void UpdateDataPeriod(DateTime from, DateTime? to)
        {
            FromDataPeriod = from;
            ToDataPeriod = to;
        }


        public override void Update(DashboardUpdate update)
        {
            base.Update(update);
        }

        public override DashboardEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.Panels.AddRange(Panels.Select(u => u.Value.ToEntity()));

            return entity;
        }
    }
}