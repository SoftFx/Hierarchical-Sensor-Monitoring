using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Dashboards
{
    public sealed record RangeSettings
    {
        [Display(Name = "Autoscale")]
        public bool AutoScale { get; set; } = true;


        [Display(Name = "Max")]
        public double MaxY { get; set; }

        [Display(Name = "Min")]
        public double MinY { get; set; }


        public void Update(PanelUpdate update)
        {
            AutoScale = update.AutoScale ?? AutoScale;

            MaxY = update.MaxY ?? MaxY;
            MinY = update.MinY ?? MinY;
        }

        public void FromEntity(ChartRangeEntity entity)
        {
            AutoScale = !entity.FixedBorders;
            MaxY = entity.MaxValue;
            MinY = entity.MinValue;
        }
    }


    public sealed class PanelSettings
    {
        internal const double DefaultHeight = 0.2;
        internal const double DefaultWidth = 0.3;

        public RangeSettings RangeSettings { get; } = new();


        public double Width { get; private set; } = DefaultWidth;

        public double Height { get; private set; } = DefaultHeight;


        public double X { get; private set; }

        public double Y { get; private set; }


        public bool ShowLegend { get; private set; }


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

            RangeSettings.Update(update);
        }

        public PanelSettings FromEntity(PanelSettingsEntity entity)
        {
            Height = entity.Height;
            Width = entity.Width;

            X = entity.X;
            Y = entity.Y;

            ShowLegend = entity.ShowLegend;

            RangeSettings.FromEntity(entity.YRangeSettings);

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

                YRangeSettings = new ChartRangeEntity
                {
                    MaxValue = RangeSettings.MaxY,
                    MinValue = RangeSettings.MinY,

                    FixedBorders = !RangeSettings.AutoScale,
                },
            };
    }
}