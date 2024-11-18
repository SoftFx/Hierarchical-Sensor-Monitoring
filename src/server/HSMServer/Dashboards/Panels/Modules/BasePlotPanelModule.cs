using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
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


    public abstract class BasePlotPanelModule<TUpdate, TEntity> : BasePanelModule<TUpdate, TEntity>
        where TUpdate : PanelSourceUpdate
        where TEntity : PlotSourceSettingsEntity, new()
    {
        public PlottedProperty Property { get; protected set; }

        public PlottedShape Shape { get; protected set; }

        public string Label { get; protected set; }

        public Color Color { get; set; }

        public bool ShowProperty { get;  set; }

        protected BasePlotPanelModule() : base()
        {
            Color = Color.FromName(ColorExtensions.GenerateRandomColor());
            Property = PlottedProperty.Value;
            Shape = PlottedShape.linear;
            ShowProperty = true;
        }

        protected BasePlotPanelModule(TEntity entity) : base(entity)
        {
            Property = (PlottedProperty)entity.Property;
            Shape = (PlottedShape)entity.Shape;
            Color = Color.FromName(entity.Color);
            Label = entity.Label;
            ShowProperty = entity.ShowProperty;
        }


        public override void Update(TUpdate update)
        {
            var changePrinciples = false;

            T ApplyDependentChange<T>(T value)
            {
                changePrinciples = true;
                return value;
            }

            Color = update.Color is not null ? Color.FromName(update.Color) : Color;
            Label = !string.IsNullOrEmpty(update.Label) ? update.Label : Label;
            ShowProperty = update.ShowProperty;
            
            if (Enum.TryParse<PlottedProperty>(update.Property, out var newProperty) && Property != newProperty)
                Property = ApplyDependentChange(newProperty);

            if (Enum.TryParse<PlottedShape>(update.Shape, out var newShape) && Shape != newShape)
                Shape = newShape;

            if (changePrinciples)
                ChangeDependentProperties(update);
        }

        protected virtual void ChangeDependentProperties(TUpdate update) { }


        public override TEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.Property = (byte)Property;
            entity.Shape = (byte)Shape;
            entity.Color = Color.Name;
            entity.Label = Label;
            entity.ShowProperty = ShowProperty;

            return entity;
        }
    }
}