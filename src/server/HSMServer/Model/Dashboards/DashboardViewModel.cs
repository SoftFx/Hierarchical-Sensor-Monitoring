using HSMServer.Dashboards;
using HSMServer.Model.Authentication;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HSMServer.Model.Dashboards
{
    public sealed class DashboardViewModel
    {
        private const string DefaultName = "New Dashboard";

        private readonly Dashboard _dashboard;


        public static readonly List<SelectListItem> Periods =
        [
            new SelectListItem("last 30 minutes", "00:30:00"),
            new SelectListItem("last 1 hour", "01:00:00"),
            new SelectListItem("last 3 hours", "03:00:00"),
            new SelectListItem("last 6 hours", "06:00:00"),
            new SelectListItem("last 12 hours", "12:00:00"),
            new SelectListItem("last 1 day", "1.00:90:00"),
            new SelectListItem("last 3 day", "3.00:00:00"),
            new SelectListItem("last 7 day", "7.00:00:00")
        ];


        public List<PanelViewModel> Panels { get; set; } = new();


        public Guid? Id { get; set; }

        [Display(Name = "Dashboard:")]
        public string Name { get; set; }

        public string Description { get; set; }

        public TimeSpan FromPeriod { get; set; }


        public bool IsModify { get; init; }


        public DashboardViewModel() { }

        public DashboardViewModel(Dashboard dashboard, bool isModify = true)
        {
            _dashboard = dashboard;

            Id = dashboard.Id;
            Name = dashboard.Name;
            Description = dashboard.Description;
            FromPeriod = dashboard.DataPeriod;
            Panels = dashboard.Panels.Select(x => new PanelViewModel(x.Value, Id.Value)).ToList();

            IsModify = isModify;
        }


        public async Task<DashboardViewModel> InitDashboardData()
        {
            var from = DateTime.UtcNow - _dashboard.DataPeriod;

            await Task.WhenAll(Panels.Select(t => t.InitPanelData(from)));

            return this;
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