using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Dashboards
{
    public sealed class PanelSettings
    {
        internal const double DefaultHeight = 0.2;
        internal const double DefaultWidth = 0.3;


        public double Width { get; private set; } = DefaultWidth;

        public double Height { get; private set; } = DefaultHeight;


        public double X { get; private set; }

        public double Y { get; private set; }


        public bool ShowLegend { get; private set; }
        
        [Display(Name = "Max")]
        public double MaxY { get; set; }
        
        [Display(Name = "Min")]
        public double MinY { get; set; }
        
        
        [Display(Name = "Autoscale")]
        public bool AutoScale { get; set; }


        public PanelSettings()
        {
            ShowLegend = true;
            AutoScale = true;
        }


        public void Update(PanelUpdate update)
        {
            Height = update.Height ?? Height;
            Width = update.Width ?? Width;

            X = update.X ?? X;
            Y = update.Y ?? Y;

            ShowLegend = update.ShowLegend ?? ShowLegend;

            MaxY = update.MaxY ?? MaxY;
            MinY = update.MinY ?? MinY;

            AutoScale = update.AutoScale ?? AutoScale;
        }

        public PanelSettings FromEntity(PanelSettingsEntity entity)
        {
            Height = entity.Height;
            Width = entity.Width;

            X = entity.X;
            Y = entity.Y;

            ShowLegend = entity.ShowLegend;

            MaxY = entity.MaxY;
            MinY = entity.MinY;

            AutoScale = entity.AutoScale;
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
                
                MaxY = MaxY,
                MinY = MinY,
                
                AutoScale = AutoScale
            };
    }
}