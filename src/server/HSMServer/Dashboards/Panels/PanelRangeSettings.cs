﻿using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.Core.Model;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Dashboards
{
    public sealed record PanelRangeSettings
    {
        [Display(Name = "Autoscale")]
        public bool AutoScale { get; set; } = true;


        [Display(Name = "Max")]
        public double MaxValue { get; set; }

        [Display(Name = "Min")]
        public double MinValue { get; set; }


        internal bool IsRangeScalePossible(SensorType sensorType) => sensorType is (SensorType.Double or SensorType.Integer or SensorType.DoubleBar or SensorType.IntegerBar or SensorType.Rate);


        public void Update(PanelUpdate update)
        {
            AutoScale = update.AutoScale ?? AutoScale;

            MaxValue = update.MaxY ?? MaxValue;
            MinValue = update.MinY ?? MinValue;
        }

        public void FromEntity(ChartRangeEntity entity)
        {
            AutoScale = !entity.FixedBorders;
            MaxValue = entity.MaxValue;
            MinValue = entity.MinValue;
        }

        public ChartRangeEntity ToEntity() =>
            new()
            {
                MaxValue = MaxValue,
                MinValue = MinValue,

                FixedBorders = !AutoScale,
            };
    }
}
