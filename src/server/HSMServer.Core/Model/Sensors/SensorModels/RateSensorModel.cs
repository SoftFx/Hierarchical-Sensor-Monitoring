using System;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Storages.ValueStorages;

namespace HSMServer.Core.Model.Sensors.SensorModels
{
    internal sealed class RateSensorModel : BaseSensorModel<RateValue>
    {
        internal override RateValuesStorage Storage { get; } = new RateValuesStorage();


        public override SensorPolicyCollection<RateValue, RatePolicy> Policies { get; } = new();

        public override SensorType Type { get; } = SensorType.Rate;


        public RateSensorModel(SensorEntity entity) : base(entity)
        {
            if (entity.DisplayUnit.HasValue)
                DisplayUnit = (RateDisplayUnit)entity.DisplayUnit;
            else if (OriginalUnit == Unit.ValueInSecond)
            {
                //default value
                DisplayUnit = RateDisplayUnit.PerSecond;
            }
        }

        protected override int GetDisplayCoeff()
        {
            if (!DisplayUnit.HasValue)
                return 1;

            return DisplayUnit switch
            {
                RateDisplayUnit.PerSecond => 1,
                RateDisplayUnit.PerMinute => 60,
                RateDisplayUnit.PerHour => 60 * 60,
                RateDisplayUnit.PerDay => 60 * 60 * 24,
                RateDisplayUnit.PerWeek => 60 * 60 * 24 * 7,
                RateDisplayUnit.PerMonth => 60 * 60 * 24 * 7 * 30,
                _ => throw new ArgumentOutOfRangeException(nameof(DisplayUnit))
            };
        }

        public override BaseValue ToDisplayValue(BaseValue value)
        {
            if (value is BaseValue<double> typedValue)
            {
                return typedValue with
                {
                    Value = typedValue.Value * GetDisplayCoeff()
                };
            }

            throw new ApplicationException(
                $"'{value.GetType()}' is not RateSensorModel value: (BaseValue<double> needed)");
        }

        //internal override BaseValue Convert(byte[] bytes)
        //{
        //    var rateValue = bytes.ToValue<RateValue>();

        //    if (rateValue is BaseValue<double> typedValue)
        //    {
        //        return typedValue with
        //        {
        //            Value = typedValue.Value * GetDisplayCoeff()
        //        };
        //    }

        //    return rateValue; 
        //}
    }
}