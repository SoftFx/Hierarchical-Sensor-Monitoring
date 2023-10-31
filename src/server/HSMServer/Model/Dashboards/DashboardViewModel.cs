using HSMServer.Dashboards;
using HSMServer.Model.Authentication;
using System;

namespace HSMServer.Model.Dashboards
{
    public sealed class DashboardViewModel
    {
        public Guid Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }


        public DashboardViewModel() { }

        public DashboardViewModel(Dashboard dashboard)
        {
            Id = dashboard.Id;
            Name = dashboard.Name;
            Description = dashboard.Description;
        }


        internal DashboardAdd ToDashboardAdd(User author) =>
            new()
            {
                Name = Name,
                AuthorId = author.Id,
                Description = Description,
            };
    }
}
