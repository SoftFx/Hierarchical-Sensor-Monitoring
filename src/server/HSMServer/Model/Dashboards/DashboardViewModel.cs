using HSMServer.Dashboards;
using HSMServer.Model.Authentication;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;
using HSMServer.Authentication;

namespace HSMServer.Model.Dashboards
{
    public sealed class DashboardViewModel
    {
        private const string DefaultName = "New Dashboard";

        private readonly Dashboard _dashboard;


        public static readonly List<SelectListItem> Periods =
        [
            new SelectListItem("last 30 minutes", TimeSpan.FromMinutes(30).ToString()),
            new SelectListItem("last 1 hour", TimeSpan.FromHours(1).ToString()),
            new SelectListItem("last 3 hours", TimeSpan.FromHours(3).ToString()),
            new SelectListItem("last 6 hours", TimeSpan.FromHours(6).ToString()),
            new SelectListItem("last 12 hours", TimeSpan.FromHours(12).ToString()),
            new SelectListItem("last 1 day", TimeSpan.FromDays(1).ToString()),
            new SelectListItem("last 3 day", TimeSpan.FromDays(3).ToString()),
            new SelectListItem("last 7 day", TimeSpan.FromDays(7).ToString()),
            new SelectListItem("last 30 days", TimeSpan.FromDays(30).ToString())
        ];


        public Guid? AuthorId { get; }

        public string Author { get; private set; }


        public List<PanelViewModel> Panels { get; set; } = new();


        public Guid? Id { get; set; }

        [Display(Name = "Dashboard:")]
        public string Name { get; set; }

        public string Description { get; set; }

        public TimeSpan FromPeriod { get; set; }


        public bool IsModify { get; init; }


        public DashboardViewModel() { }

        public DashboardViewModel(Dashboard dashboard, Dictionary<Guid, string> availableFolders, bool isModify = true)
        {
            _dashboard = dashboard;

            Id = dashboard.Id;
            Name = dashboard.Name;
            AuthorId = dashboard.AuthorId;
            Description = dashboard.Description;
            FromPeriod = dashboard.DataPeriod;
            Panels = dashboard.Panels.Select(x => new PanelViewModel(x.Value, Id.Value, availableFolders)).ToList();

            IsModify = isModify;
        }


        public async Task<DashboardViewModel> InitDashboardData()
        {
            var from = DateTime.UtcNow - _dashboard.DataPeriod;

            await Task.WhenAll(Panels.Select(t => t.InitPanelData(from)));

            return this;
        }

        internal DashboardViewModel AttachUser(IUserManager userManager)
        {
            if (userManager.TryGetValueById(AuthorId, out User user))
                Author = user.Name;

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