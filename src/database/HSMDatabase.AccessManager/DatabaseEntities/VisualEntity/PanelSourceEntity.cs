namespace HSMDatabase.AccessManager.DatabaseEntities.VisualEntity
{
    public record PanelSourceEntity
    {
        public byte[] Id { get; init; }

        public byte[] SensorId { get; init; }


        public int Color { get; init; }


        public string Label { get; init; }
    }
}