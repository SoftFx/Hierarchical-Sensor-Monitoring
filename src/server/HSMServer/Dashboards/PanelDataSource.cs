using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.Core;
using HSMServer.Core.Model;
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


    public sealed class PanelDatasource : IDisposable
    {
        public BaseSensorModel Sensor { get; }

        public Guid SensorId { get; }

        public Guid Id { get; }


        public SensorDatasourceBase Source { get; private set; }


        public PlottedProperty Property { get; private set; }

        public string Label { get; private set; }

        public Color Color { get; private set; }

        public bool AggragateValues { get; private set; }


        internal event Action UpdateEvent;


        public PanelDatasource(BaseSensorModel sensor)
        {
            Id = Guid.NewGuid();
            SensorId = sensor.Id;

            Sensor = sensor;

            Color = Color.FromName(ColorExtensions.GenerateRandomColor());
            Property = sensor.Type.IsBar() ? PlottedProperty.Max : PlottedProperty.Value;
            Label = $"{sensor.DisplayName} ({Property})";

            AggragateValues = true;
        }

        public PanelDatasource(BaseSensorModel sensor, PanelSourceEntity entity) : this(sensor)
        {
            Id = new Guid(entity.Id);
            SensorId = new Guid(entity.SensorId);

            Property = (PlottedProperty)entity.Property;
            Color = Color.FromName(entity.Color);
            Label = entity.Label;

            AggragateValues = entity.IsAggregate;
        }


        public PanelDatasource BuildSource()
        {
            Source?.Dispose(); // unsubscribe prev version

            var settings = new SourceSettings
            {
                SensorType = Sensor.Type,
                Property = Property,

                AggregateValues = AggragateValues,
            };

            Source = DatasourceFactory.Build(Sensor, settings);

            return this;
        }


        public PanelDatasource Update(PanelSourceUpdate update)
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

            if (update.IsAggregateValues != AggragateValues)
                AggragateValues = ApplyRebuild(update.IsAggregateValues);

            UpdateEvent?.Invoke();

            if (rebuildSource)
                BuildSource();

            return this;
        }


        public PanelSourceEntity ToEntity() =>
            new()
            {
                Id = Id.ToByteArray(),
                SensorId = SensorId.ToByteArray(),

                IsAggregate = AggragateValues,
                Property = (byte)Property,
                Color = Color.Name,
                Label = Label,
            };

        public void Dispose()
        {
            Source?.Dispose();
            UpdateEvent = null;
        }
    }
}