using HSMServer.Dashboards;
using HSMServer.Model.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.Dashboards
{
    public sealed class DashboardViewModel
    {
        private const string DefaultName = "New Dashboard";

        public List<PanelViewModel> Panels { get; set; } = new();


        public Guid? Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }
        
        public TimeSpan FromPeriod { get; set; }


        public bool IsModify { get; init; }


        public DashboardViewModel() { }

        public DashboardViewModel(Dashboard dashboard, bool isModify = true)
        {
            Id = dashboard.Id;
            Name = dashboard.Name;
            Description = dashboard.Description;
            Panels = dashboard.Panels.Select(x => new PanelViewModel(x.Value, Id.Value)).ToList();

            IsModify = isModify;
        }


        internal DashboardUpdate ToDashboardUpdate() =>
            new()
            {
                Id = Id.Value,
                Name = Name,
                Description = Description,
                FromPeriod = FromPeriod
            };

        internal static DashboardAdd ToDashboardAdd(User author) =>
            new()
            {
                Name = DefaultName,
                AuthorId = author.Id,
            };

    }
}
