using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities.VisualEntity
{
    public record DashboardPanelEntity : BaseServerEntity
    {
        public List<PanelSourceEntity> Sources { get; init; } = new();

        public PanelPositionEntity Position { get; set; }
    }


    public record PanelPositionEntity
    {
        public double Width { get; set; } = 300;

        public double Height { get; set; } = 300;


        public double X { get; set; }

        public double Y { get; set; }
    }
}