using System;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Extensions;
using HSMCommon.Model;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HSMServer.Model.DataAlerts
{
    public sealed class TimeToLiveConditionViewModel : ConditionViewModel
    {
        private readonly SensorType? _sensorType;


        // TTL alerts expose the underlying sensor type's regular property list so a saved TTL
        // alert can be demoted back to a regular condition (#1207). null/Boolean/AnyType fall
        // back to Common. _sensorType is null during base ctor, so the body rebuilds the list.
        protected override IReadOnlyList<AlertProperty> Properties => SelectProperties(_sensorType);


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

                // Base ctor left Property = Common's first item (Status); keep it consistent with
                // the rebuilt list. FillConditions overwrites this to TimeToLive for actual TTL alerts.
                Property = Enum.Parse<AlertProperty>(PropertiesItems.First().Value);
            }
        }


        private static IReadOnlyList<AlertProperty> SelectProperties(SensorType? sensorType) => sensorType switch
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
