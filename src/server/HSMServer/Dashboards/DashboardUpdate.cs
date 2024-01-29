using HSMServer.ConcurrentStorage;
using System;
using System.Diagnostics.CodeAnalysis;

namespace HSMServer.Dashboards
{
    public record DashboardUpdate : BaseUpdateRequest
    {
        public TimeSpan FromPeriod { get; set; }
    }


    public record PanelUpdate : BaseUpdateRequest
    {
        public double? Width { get; init; }

        public double? Height { get; init; }


        public double? X { get; init; }

        public double? Y { get; init; }


        public bool? ShowLegend { get; init; }

        public bool ShowProduct { get; init; }


        [SetsRequiredMembers]
        public PanelUpdate(Guid panelId) : base()
        {
            Id = panelId;
        }
    }


    public record PanelSourceUpdate(string Name, string Color, string Property, bool IsAggregateValues = true);
}