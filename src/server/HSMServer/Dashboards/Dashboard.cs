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
        private static readonly TimeSpan _defaultPeriod = new(0, 30, 0);


        public ConcurrentDictionary<Guid, Panel> Panels { get; } = new();


        public DateTime FromDataPeriod { get; private set; }

        public DateTime? ToDataPeriod { get; private set; }

        public TimeSpan DataPeriod { get; private set; } = new(0, 30, 0);


        internal Func<Guid, BaseSensorModel> GetSensorModel;


        internal Dashboard(DashboardAdd addModel) : base(addModel) { }

        internal Dashboard(DashboardEntity entity, Func<Guid, BaseSensorModel> getSensorModel) : base(entity)
        {
            GetSensorModel += getSensorModel;

            Panels = new ConcurrentDictionary<Guid, Panel>(entity.Panels.ToDictionary(k => new Guid(k.Id), v => new Panel(v, this)));
            DataPeriod = GetPeriod(entity.DataPeriod);
        }


        public void UpdateDataPeriod(DateTime from, DateTime? to)
        {
            FromDataPeriod = from;
            ToDataPeriod = to;
        }


        public override void Update(DashboardUpdate update)
        {
            DataPeriod = update.FromPeriod;
            base.Update(update);
        }

        public override DashboardEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.Panels.AddRange(Panels.Select(u => u.Value.ToEntity()));
            entity.DataPeriod = DataPeriod;
            return entity;
        }


        private static TimeSpan GetPeriod(TimeSpan entityPeriod) => entityPeriod == TimeSpan.Zero ? _defaultPeriod : entityPeriod;
    }
}