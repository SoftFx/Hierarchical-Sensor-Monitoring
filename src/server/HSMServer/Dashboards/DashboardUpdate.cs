﻿using HSMServer.ConcurrentStorage;
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


        public bool? IsSingleMode { get; init; }
        
        public bool? ShowLegend { get; init; }

        public bool? ShowProduct { get; init; }

        public bool? IsAggregateValues { get; init; }

        public double? MaxY { get; init; }

        public double? MinY { get; init; }

        public bool? AutoScale { get; set; }

        public TooltipHovermode? Hovermode { get; init; }


        public bool NeedSourceRebuild => IsAggregateValues.HasValue || MinY.HasValue || MaxY.HasValue || AutoScale.HasValue;


        [SetsRequiredMembers]
        public PanelUpdate(Guid panelId) : base()
        {
            Id = panelId;
        }
    }


    public sealed record PanelUpdateDto
    {
        public TooltipHovermode? Hovermode { get; set; }
        
        public bool? IsSingleMode { get; set; }
        
        public bool? ShowLegend { get; set; } 
        
        public double? X { get; set; }
        
        public double? Y { get; set; }
        
        public double? Height { get; set; }
        
        public double? Width { get; set; }
        
        

        internal PanelUpdate ToUpdate(Guid id) =>
            new(id)
            {
                Hovermode = Hovermode,
                IsSingleMode = IsSingleMode,
                ShowLegend = ShowLegend,
                X = X,
                Y = Y,
                Height = Height,
                Width = Width
            };
    }


    public record PanelSourceUpdate
    {
        public PanelRangeSettings YRange { get; init; }

        public string Label { get; init; }

        public string Color { get; init; }

        public string Property { get; init; }

        public string Shape { get; init; }

        public bool AggregateValues { get; init; }
        
        public bool IsSingleMode { get; init; }
    }


    public sealed record PanelSubscriptionUpdate : PanelSourceUpdate
    {
        public SubscriptionFoldersUpdate FoldersFilter { get; init; }

        public string PathTemplate { get; init; }

        public bool? IsSubscribed { get; init; }
    }


    public record SubscriptionFoldersUpdate(HashSet<Guid> Folders);
}