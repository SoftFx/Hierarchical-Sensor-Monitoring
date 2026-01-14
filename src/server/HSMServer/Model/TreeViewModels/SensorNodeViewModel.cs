using System;
using System.Linq;
using System.Collections.Generic;
using HSMServer.Core;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensors;
using HSMServer.Extensions;
using HSMServer.Model.DataAlerts;
using HSMCommon.Model;


namespace HSMServer.Model.TreeViewModel
{
    public class SensorNodeViewModel : NodeViewModel
    {
        private const string ExtensionPattern = "Extension: ";
        private const string FileNamePattern = "File name: ";
        private const string ServiceAliveName = "Service alive";
        private const string ServiceStatusName = "Service status";

        public const int ValuesLimit = 4000;

        public SensorType Type { get; private set; }

        public SensorState State { get; private set; }

        public Integration Integration { get; private set; }

        public string ShortStringValue { get; private set; }

        public string FileNameString { get; private set; }

        internal BaseValue LastValue { get; private set; }

        internal BaseValue LastTimeout { get; private set; }

        public string ValidationError { get; private set; }

        public bool AggregateValues { get; private set; }

        public bool IsSingleton { get; private set; }

        public bool IsEma { get; private set; }


        public List<Unit> AvailableUnits { get; private set; }

        public Unit? SelectedUnit { get; private set; }


        public bool IsValidationErrorVisible => !string.IsNullOrEmpty(ValidationError);

        public bool IsChartSupported => Type is not SensorType.String;

        public bool IsTableFormatSupported => Type is not SensorType.File;

        public bool IsDatapointFormatSupported => Type is SensorType.Integer or SensorType.Double or SensorType.Rate or SensorType.Boolean
                                                  or SensorType.String or SensorType.TimeSpan;

        public bool IsServiceAlive => Name == ServiceAliveName;
        public bool IsServiceStatus => Name == ServiceStatusName;

        public RateDisplayUnit? DisplayUnit { get; private set; }

        public DateTime CreationTime { get; private set; }

        public Dictionary<int, EnumOptionModel> EnumOptions { get; private set; }

        private BaseSensorModel _model;

        public BaseValue ToDisplayValue(BaseValue value) => _model.ToDisplayValue(value);

        public SensorNodeViewModel(BaseSensorModel model) : base(model) 
        {
            _model = model;
            EnumOptions = model.EnumOptions;
            DisplayUnit = model.DisplayUnit;
        }


        internal void Update(BaseSensorModel model)
        {
            base.Update(model);

            _model = model;
            Type = model.Type;
            State = model.State;
            IsEma = model.Statistics.HasEma();
            Integration = model.Integration;
            UpdateTime = model.LastUpdate;
            IsSingleton = model.IsSingleton;
            Status = model.Status.ToClient();
            SelectedUnit = model.OriginalUnit;
            AggregateValues = model.AggregateValues;
            CreationTime = model.CreationDate;
            EnumOptions = model.EnumOptions;
            DisplayUnit = model.DisplayUnit;

            if (State is SensorState.Muted)
                ValidationError = GetMutedErrorTooltip(model.EndOfMuting);
            else if (model.Status?.HasError ?? false)
                ValidationError = model.Status?.Message;
            else
                ValidationError = string.Empty;

            LastTimeout = model.LastTimeout;
            LastValue = model.LastValue;

            HasData = model.HasData;
            ShortStringValue = model.LastValue?.ShortInfo;

            FileNameString = GetFileNameString(model.Type, ShortStringValue);

            DataAlerts[(byte)Type] = model.Policies.Select(x => DataAlertViewModel.BuildAlert(x, this)).ToList();

            AlertIcons.Clear();
            foreach (var alert in model.PolicyResult)
            {
                var icon = alert.Icon;
                if (icon is null)
                    continue;

                if (!AlertIcons.ContainsKey(icon))
                    AlertIcons.TryAdd(icon, 0);

                AlertIcons[icon] += alert.Count;
            }
        }

        private static string GetFileNameString(SensorType sensorType, string value)
        {
            if (sensorType != SensorType.File || string.IsNullOrEmpty(value))
                return string.Empty;

            var ind = value.IndexOf(FileNamePattern);
            if (ind != -1)
            {
                var fileNameString = value[(ind + FileNamePattern.Length)..];
                int firstDotIndex = fileNameString.IndexOf('.');
                int secondDotIndex = fileNameString[(firstDotIndex + 1)..].IndexOf('.');
                return fileNameString[..(firstDotIndex + secondDotIndex + 1)];
            }

            ind = value.IndexOf(ExtensionPattern);
            if (ind != -1)
            {
                var extensionString = value[(ind + ExtensionPattern.Length)..];
                int dotIndex = extensionString.IndexOf('.');
                return extensionString[..dotIndex];
            }

            return string.Empty;
        }

        private static string GetMutedErrorTooltip(DateTime? endOfMuting) =>
            endOfMuting is not null && endOfMuting != DateTime.MaxValue
                ? $"Muted until {endOfMuting.Value.ToDefaultFormat()}"
                : $"Muted forever";
    }
}
