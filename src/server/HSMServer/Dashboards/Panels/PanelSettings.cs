using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Dashboards
{
    public enum TooltipHovermode : byte
    {
        [Display(Name = "x")]
        X,
        [Display(Name = "y")]
        Y,
        [Display(Name = "false")]
        False,
        [Display(Name = "closest")]
        Closest,
        [Display(Name = "x unified")]
        XUnified,
        [Display(Name = "y unified")]
        YUnified,
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

        public TooltipHovermode Hovermode { get; private set; } = TooltipHovermode.X;


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
        }

        public PanelSettings FromEntity(PanelSettingsEntity entity)
        {
            Height = entity.Height;
            Width = entity.Width;

            X = entity.X;
            Y = entity.Y;

            ShowLegend = entity.ShowLegend;

            Hovermode = (TooltipHovermode)entity.Hovermode;
            
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
                
                Hovermode = (byte)Hovermode,
            };
    }
}