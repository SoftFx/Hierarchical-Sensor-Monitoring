using HSMServer.Dashboards;
using HSMServer.Model.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.Dashboards
{
    public sealed class DashboardViewModel
    {
        public List<PanelViewModel> Panels { get; set; } = new();


        public Guid? Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }


        public DashboardViewModel() { }

        public DashboardViewModel(Dashboard dashboard)
        {
            Id = dashboard.Id;
            Name = dashboard.Name;
            Description = dashboard.Description;
            Panels = dashboard.Panels.Select(x => new PanelViewModel(x.Value, Id.Value)).ToList();
        }


        internal DashboardAdd ToDashboardAdd(User author) =>
            new()
            {
                Name = Name,
                AuthorId = author.Id,
                Description = Description,
            };

        internal DashboardUpdate ToDashboardUpdate() =>
            new()
            {
                Id = Id.Value,
                Name = Name,
                Description = Description,
            };
    }
}
