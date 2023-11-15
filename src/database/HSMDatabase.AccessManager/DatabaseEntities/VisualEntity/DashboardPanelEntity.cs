using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities.VisualEntity
{
    public record DashboardPanelEntity : BaseServerEntity
    {
        public List<PanelSourceEntity> Sources { get; init; } = new();
        
        public CordsEntity Cords { get; set; }
    }

    public record CordsEntity(double Width = 300, double Height = 300, double X = 0, double Y = 0);
}