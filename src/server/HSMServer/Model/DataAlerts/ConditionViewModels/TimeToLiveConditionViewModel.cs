using System.Collections.Generic;
using HSMCommon.Extensions;
using HSMCommon.Model;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HSMServer.Model.DataAlerts
{
    public sealed class TimeToLiveConditionViewModel : ConditionViewModel
    {
        private readonly SensorType? _sensorType;


        // For TTL alerts loaded from storage (#1207), the underlying sensor type determines
        // which regular properties the user can demote to. null / Boolean / AnyType fall back
        // to the Common subset so the dropdown always offers at least Status/Comment/New data.
        // During the base ctor _sensorType is still null (Common list); the body rebuilds
        // PropertiesItems once a non-default sensor type is supplied.
        protected override List<AlertProperty> Properties => SelectProperties(_sensorType);


        public TimeToLiveConditionViewModel() : this(null, true) { }

        public TimeToLiveConditionViewModel(bool isMain) : this(null, isMain) { }

        public TimeToLiveConditionViewModel(SensorType? sensorType, bool isMain = true) : base(isMain)
        {
            _sensorType = sensorType;

            if (sensorType is not null and not SensorType.Boolean)
            {
                PropertiesItems.Clear();
                PropertiesItems.AddRange(Properties.ToSelectedItems(k => k.GetDisplayName()));

                if (!isMain)
                    PropertiesItems.Add(new SelectListItem(AlertProperty.ConfirmationPeriod.GetDisplayName(), nameof(AlertProperty.ConfirmationPeriod)));
            }
        }


        private static List<AlertProperty> SelectProperties(SensorType? sensorType) => sensorType switch
        {
            SensorType.Integer or SensorType.Double or SensorType.Rate or SensorType.Enum => NumericConditionViewModel.SupportedProperties,
            SensorType.Version => VersionConditionViewModel.SupportedProperties,
            SensorType.TimeSpan => SingleConditionViewModel.SupportedProperties,
            SensorType.String => StringConditionViewModel.SupportedProperties,
            SensorType.File => FileConditionViewModel.SupportedProperties,
            SensorType.IntegerBar or SensorType.DoubleBar => BarConditionViewModel.SupportedProperties,
            _ => CommonConditionViewModel.SupportedProperties,
        };
    }
}
