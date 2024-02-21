using HSMServer.ConcurrentStorage;
using System;
using System.Collections.Generic;
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

        public bool? ShowProduct { get; init; }

        public bool? IsAggregateValues { get; init; }

        public double? MaxY { get; init; }

        public double? MinY { get; init; }

        public bool? AutoScale { get; set; }


        public bool NeedSourceRebuild => IsAggregateValues.HasValue || MinY.HasValue || MaxY.HasValue || AutoScale.HasValue;


        [SetsRequiredMembers]
        public PanelUpdate(Guid panelId) : base()
        {
            Id = panelId;
        }
    }


    public record PanelSourceUpdate
    {
        public PanelRangeSettings YRange { get; init; }

        public string Label { get; init; }

        public string Color { get; init; }

        public string Property { get; init; }

        public string Shape { get; init; }

        public bool AggregateValues { get; init; }
    }


    public record PanelSubscriptionUpdate : PanelSourceUpdate
    {
        public List<Guid> Folders { get; init; }

        public string PathTemplate { get; init; }

        public bool? IsSubscribed { get; init; }
    }
}