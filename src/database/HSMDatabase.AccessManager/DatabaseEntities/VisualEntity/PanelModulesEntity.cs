using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities.VisualEntity
{
    public sealed record PanelSubscriptionEntity : PlotSourceSettingsEntity
    {
        public HashSet<Guid> Folders { get; set; }

        public string PathTemplate { get; set; }


        public bool IsSubscribed { get; set; }

        public bool IsApplied { get; set; }
    }


    public sealed record PanelSourceEntity : PlotSourceSettingsEntity
    {
        public byte[] SensorId { get; set; }
    }


    public abstract record PlotSourceSettingsEntity : PanelBaseModuleEntity
    {
        public string Color { get; set; }

        public string Label { get; set; }

        public byte Property { get; set; }

        public byte Shape { get; set; }
    }


    public abstract record PanelBaseModuleEntity
    {
        public byte[] Id { get; init; }
    }
}