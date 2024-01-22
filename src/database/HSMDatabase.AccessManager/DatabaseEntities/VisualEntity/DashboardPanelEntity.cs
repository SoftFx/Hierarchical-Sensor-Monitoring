using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities.VisualEntity
{
    public sealed record DashboardPanelEntity : BaseServerEntity
    {
        public List<PanelSubscriptionsEntity> Subsctiptions { get; init; } = new();

        public List<PanelSourceEntity> Sources { get; init; } = new();


        public PanelSettingsEntity Settings { get; set; }
    }


    public sealed record PanelSettingsEntity
    {
        public double Width { get; init; }

        public double Height { get; init; }


        public double X { get; init; }

        public double Y { get; init; }


        public bool ShowLegend { get; init; }
    }


    public sealed record PanelSubscriptionsEntity : PlotSourceSettingsEntity
    {
        public byte[] Id { get; init; }

        public string PathTemplate { get; init; }
    }
}