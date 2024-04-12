using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;

namespace HSMServer.Dashboards
{
    public sealed class PanelSettings
    {
        private const string DefaultHovermode = "x";

        internal const double DefaultHeight = 0.2;
        internal const double DefaultWidth = 0.3;


        public double Width { get; private set; } = DefaultWidth;

        public double Height { get; private set; } = DefaultHeight;


        public double X { get; private set; }

        public double Y { get; private set; }


        public bool ShowLegend { get; private set; }

        public string Hovermode { get; private set; } = DefaultHovermode;

        public int HoverDistance { get; private set; } = 1;
 

        public PanelSettings()
        {
            ShowLegend = true;
        }


        public void Update(PanelUpdate update)
        {
            Height = update.Height ?? Height;
            Width = update.Width ?? Width;

            X = update.X ?? X;
            Y = update.Y ?? Y;

            ShowLegend = update.ShowLegend ?? ShowLegend;

            Hovermode = update.Hovermode ?? Hovermode;
            HoverDistance = update.HoverDistance ?? HoverDistance;
        }

        public PanelSettings FromEntity(PanelSettingsEntity entity)
        {
            Height = entity.Height;
            Width = entity.Width;

            X = entity.X;
            Y = entity.Y;

            ShowLegend = entity.ShowLegend;

            Hovermode = entity.Hovermode;
            HoverDistance = entity.HoverDistance;
            
            return this;
        }

        public PanelSettingsEntity ToEntity() =>
            new()
            {
                Height = Height,
                Width = Width,

                X = X,
                Y = Y,

                ShowLegend = ShowLegend,
                
                Hovermode = Hovermode,
                HoverDistance = HoverDistance
            };
    }
}