using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.Core;
using HSMServer.Core.Model;
using HSMServer.Dashboards.Panels.Modules;
using HSMServer.Datasources;
using HSMServer.Extensions;
using System;
using System.ComponentModel.DataAnnotations;
using System.Drawing;

namespace HSMServer.Dashboards
{
    public enum PlottedProperty : byte
    {
        Value = 0,

        Bar = 50,
        Min = 51,
        Mean = 52,
        Max = 53,
        FirstValue = 54,
        LastValue = 55,
        Count = 56,

        [Display(Name = "EMA (Value)")]
        EmaValue = 200,
        [Display(Name = "EMA (Min)")]
        EmaMin = 201,
        [Display(Name = "EMA (Mean)")]
        EmaMean = 202,
        [Display(Name = "EMA (Max)")]
        EmaMax = 203,
        [Display(Name = "EMA (Count)")]
        EmaCount = 204,
    }


    public sealed class PanelDatasource : BasePanelModule<PanelSourceUpdate, PanelSourceEntity>
    {
        public BaseSensorModel Sensor { get; }

        public Guid SensorId { get; }


        public SensorDatasourceBase Source { get; private set; }


        public PlottedProperty Property { get; private set; }

        public string Label { get; private set; }

        public Color Color { get; private set; }


        public PanelDatasource(BaseSensorModel sensor) : base()
        {
            SensorId = sensor.Id;
            Sensor = sensor;

            Color = Color.FromName(ColorExtensions.GenerateRandomColor());
            Property = sensor.Type.IsBar() ? PlottedProperty.Max : PlottedProperty.Value;
            Label = $"{sensor.DisplayName} ({Property})";
        }

        public PanelDatasource(BaseSensorModel sensor, PanelSourceEntity entity) : base(entity)
        {
            SensorId = new Guid(entity.SensorId);
            Sensor = sensor;

            Property = (PlottedProperty)entity.Property;
            Color = Color.FromName(entity.Color);
            Label = entity.Label;
        }


        public PanelDatasource BuildSource()
        {
            Source?.Dispose(); // unsubscribe prev version
            Source = DatasourceFactory.Build(Sensor, Property);

            return this;
        }


        protected override void ApplyUpdate(PanelSourceUpdate update)
        {
            Color = update.Color is not null ? Color.FromName(update.Color) : Color;
            Label = !string.IsNullOrEmpty(update.Name) ? update.Name : Label;

            if (Enum.TryParse<PlottedProperty>(update.Property, out var newProperty) && Property != newProperty)
            {
                Property = newProperty;
                BuildSource();
            }
        }


        public override PanelSourceEntity ToEntity() =>
            new()
            {
                Id = Id.ToByteArray(),
                SensorId = SensorId.ToByteArray(),

                Property = (byte)Property,
                Color = Color.Name,
                Label = Label,
            };

        public override void Dispose()
        {
            base.Dispose();

            Source?.Dispose();
        }
    }
}