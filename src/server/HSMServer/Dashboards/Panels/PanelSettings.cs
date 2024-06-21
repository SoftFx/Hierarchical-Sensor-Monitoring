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

        public double SingleModeWidth { get; private set; } = DefaultWidth;

        

        public double X { get; private set; }

        public double Y { get; private set; }


        public bool ShowLegend { get; private set; }

        public TooltipHovermode Hovermode { get; private set; } = TooltipHovermode.X;

        public bool IsSingleMode { get; private set; } = false;


        public PanelSettings()
        {
            ShowLegend = true;
        }


        public void Update(PanelUpdate update)
        {
            X = update.X ?? X;
            Y = update.Y ?? Y;

            ShowLegend = update.ShowLegend ?? ShowLegend;

            Hovermode = update.Hovermode ?? Hovermode;
            IsSingleMode = update.IsSingleMode ?? IsSingleMode;

            if (!IsSingleMode)
                Width = update.Width ?? Width;
            else
                SingleModeWidth = update.Width ?? SingleModeWidth;
            
            Height = update.Height ?? Height;
        }

        public PanelSettings FromEntity(PanelSettingsEntity entity)
        {
            Height = entity.Height;
            Width = entity.Width;
            
            SingleModeWidth = entity.SWidth;

            X = entity.X;
            Y = entity.Y;

            ShowLegend = entity.ShowLegend;

            Hovermode = (TooltipHovermode)entity.Hovermode;
            IsSingleMode = entity.IsSingleMode;
            
            return this;
        }

        public PanelSettingsEntity ToEntity() =>
            new()
            {
                Height = Height,
                Width = Width,
                
                SWidth = SingleModeWidth,

                X = X,
                Y = Y,

                ShowLegend = ShowLegend,
                
                Hovermode = (byte)Hovermode,
                IsSingleMode = IsSingleMode
            };
    }
}