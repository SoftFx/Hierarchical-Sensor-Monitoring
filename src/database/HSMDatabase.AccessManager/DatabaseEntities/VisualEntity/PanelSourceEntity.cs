namespace HSMDatabase.AccessManager.DatabaseEntities.VisualEntity
{
    public record PanelSourceEntity
    {
        public byte[] Id { get; init; }

        public byte[] SensorId { get; init; }


        public string Color { get; init; }

        public string Label { get; init; }

        public byte Property { get; init; }

        public bool IsAggregate { get; init; } = true;
    }
}