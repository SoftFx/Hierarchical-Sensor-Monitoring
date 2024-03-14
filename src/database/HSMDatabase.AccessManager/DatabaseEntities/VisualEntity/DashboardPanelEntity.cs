﻿using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities.VisualEntity
{
    public sealed record DashboardPanelEntity : BaseServerEntity
    {
        public List<PanelSubscriptionEntity> Subscriptions { get; init; } = [];

        public List<PanelSourceEntity> Sources { get; init; } = [];


        public ChartRangeEntity YRangeSettings { get; set; } = new();

        public PanelSettingsEntity Settings { get; set; }

        public bool IsNotAggregate { get; set; }

        public bool ShowProduct { get; set; }
    }


    public sealed record PanelSettingsEntity
    {
        public double Width { get; init; }

        public double Height { get; init; }


        public double X { get; init; }

        public double Y { get; init; }


        public bool ShowLegend { get; init; }
    }


    public sealed record ChartRangeEntity
    {
        public double MaxValue { get; set; }

        public double MinValue { get; set; }

        public bool FixedBorders { get; set; }
    }
}