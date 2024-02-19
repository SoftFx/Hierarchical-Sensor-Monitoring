using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Dashboards
{
    public sealed record RangeSettings
    {
        [Display(Name = "Max")]
        public double MaxY { get; set; }
        
        [Display(Name = "Min")]
        public double MinY { get; set; }
        
        [Display(Name = "Autoscale")]
        public required bool AutoScale { get; set; }
    }
    
    public sealed class PanelSettings
    {
        internal const double DefaultHeight = 0.2;
        internal const double DefaultWidth = 0.3;


        public double Width { get; private set; } = DefaultWidth;

        public double Height { get; private set; } = DefaultHeight;


        public double X { get; private set; }

        public double Y { get; private set; }


        public bool ShowLegend { get; private set; }
        
        public RangeSettings RangeSettings { get; set; }


        public PanelSettings()
        {
            ShowLegend = true;
            RangeSettings = new RangeSettings()
            {
                AutoScale = true
            };
        }


        public void Update(PanelUpdate update)
        {
            Height = update.Height ?? Height;
            Width = update.Width ?? Width;

            X = update.X ?? X;
            Y = update.Y ?? Y;

            ShowLegend = update.ShowLegend ?? ShowLegend;

            RangeSettings.MaxY = update.MaxY ?? RangeSettings.MaxY;
            RangeSettings.MinY = update.MinY ?? RangeSettings.MinY;

            RangeSettings.AutoScale = update.AutoScale ?? RangeSettings.AutoScale;
        }

        public PanelSettings FromEntity(PanelSettingsEntity entity)
        {
            Height = entity.Height;
            Width = entity.Width;

            X = entity.X;
            Y = entity.Y;

            ShowLegend = entity.ShowLegend;

            RangeSettings = new RangeSettings
            {
                AutoScale = entity.AutoScale,
                MaxY = entity.MaxY,
                MinY = entity.MinY
            };
            
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
                
                MaxY = RangeSettings.MaxY,
                MinY = RangeSettings.MinY,
                
                AutoScale = RangeSettings.AutoScale
            };
    }
}