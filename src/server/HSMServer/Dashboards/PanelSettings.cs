using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using System;

namespace HSMServer.Dashboards
{
    public sealed class PanelSettings
    {
        public double Width { get; private set; } = 0.3;

        public double Height { get; private set; } = 0.2;


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
        }

        public PanelSettings FromEntity(PanelSettingsEntity entity)
        {
            Height = entity.Height;
            Width = entity.Width;

            X = entity.X;
            Y = entity.Y;

            ShowLegend = entity.ShowLegend;

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
            };
    }
}