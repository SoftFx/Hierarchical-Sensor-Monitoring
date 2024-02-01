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


    public enum PlottedShape : byte
    {
        linear = 0,
        spline = 1,
        hv = 2,
        vh = 3,
        hvh = 4,
        vhv = 5,
    }


    public sealed class PanelDatasource : BasePanelModule<PanelSourceUpdate, PanelSourceEntity>
    {
        public BaseSensorModel Sensor { get; }

        public Guid SensorId { get; }


        public SensorDatasourceBase Source { get; private set; }


        public PlottedProperty Property { get; private set; }

        public PlottedShape Shape { get; private set; }

        public string Label { get; private set; }

        public Color Color { get; private set; }


        public PanelDatasource(BaseSensorModel sensor) : base()
        {
            SensorId = sensor.Id;
            Sensor = sensor;

            Color = Color.FromName(ColorExtensions.GenerateRandomColor());
            Property = sensor.Type.IsBar() ? PlottedProperty.Max : PlottedProperty.Value;
            Label = $"{sensor.DisplayName} ({Property})";
            Shape = PlottedShape.linear;
        }

        public PanelDatasource(BaseSensorModel sensor, PanelSourceEntity entity) : base(entity)
        {
            SensorId = new Guid(entity.SensorId);
            Sensor = sensor;

            Property = (PlottedProperty)entity.Property;
            Shape = (PlottedShape)entity.Shape;
            Color = Color.FromName(entity.Color);
            Label = entity.Label;
        }


        public PanelDatasource BuildSource(bool aggregateValues)
        {
            Source?.Dispose(); // unsubscribe prev version

            var settings = new SourceSettings
            {
                SensorType = Sensor.Type,
                Property = Property,

                AggregateValues = aggregateValues,
            };

            Source = DatasourceFactory.Build(Sensor, settings);

            return this;
        }


        protected override void ApplyUpdate(PanelSourceUpdate update)
        {
            var rebuildSource = false;

            T ApplyRebuild<T>(T value)
            {
                rebuildSource = true;
                return value;
            }

            Color = update.Color is not null ? Color.FromName(update.Color) : Color;
            Label = !string.IsNullOrEmpty(update.Name) ? update.Name : Label;

            if (Enum.TryParse<PlottedProperty>(update.Property, out var newProperty) && Property != newProperty)
                Property = ApplyRebuild(newProperty);

            if (Enum.TryParse<PlottedShape>(update.Shape, out var newShape) && Shape != newShape)
                Shape = newShape;

            if (rebuildSource)
                BuildSource(update.AggregateValues);
        }


        public override PanelSourceEntity ToEntity() =>
            new()
            {
                Id = Id.ToByteArray(),
                SensorId = SensorId.ToByteArray(),

                Property = (byte)Property,
                Shape = (byte)Shape,
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