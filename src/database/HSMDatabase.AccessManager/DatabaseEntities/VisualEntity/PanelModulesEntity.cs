namespace HSMDatabase.AccessManager.DatabaseEntities.VisualEntity
{
    public sealed record PanelSubscriptionEntity : PlotSourceSettingsEntity
    {
        public string PathTemplate { get; init; }
    }


    public sealed record PanelSourceEntity : PlotSourceSettingsEntity
    {
        public byte[] SensorId { get; init; }
    }


    public record PlotSourceSettingsEntity : PanelBaseModuleEntity
    {
        public string Color { get; init; }

        public string Label { get; init; }

        public byte Property { get; init; }
    }


    public abstract record PanelBaseModuleEntity
    {
        public byte[] Id { get; init; }
    }
}