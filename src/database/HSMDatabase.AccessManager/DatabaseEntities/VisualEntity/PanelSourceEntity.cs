namespace HSMDatabase.AccessManager.DatabaseEntities.VisualEntity
{
    public sealed record PanelSourceEntity : PlotSourceSettingsEntity
    {
        public byte[] Id { get; init; }

        public byte[] SensorId { get; init; }
    }


    public record PlotSourceSettingsEntity
    {
        public string Color { get; init; }

        public string Label { get; init; }

        public byte Property { get; init; }
    }
}