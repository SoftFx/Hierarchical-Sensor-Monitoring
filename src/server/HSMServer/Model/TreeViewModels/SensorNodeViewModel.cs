﻿using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using HSMServer.Model.DataAlerts;
using System;
using System.Linq;

namespace HSMServer.Model.TreeViewModel
{
    public class SensorNodeViewModel : NodeViewModel
    {
        private const string ExtensionPattern = "Extension: ";
        private const string FileNamePattern = "File name: ";


        public SensorType Type { get; private set; }

        public SensorState State { get; private set; }

        public Integration Integration { get; private set; }

        public string ShortStringValue { get; private set; }

        public string FileNameString { get; private set; }

        internal BaseValue LastValue { get; private set; }

        public string ValidationError { get; private set; }


        public bool IsValidationErrorVisible => !string.IsNullOrEmpty(ValidationError);

        public bool IsChartSupported => Type is not (SensorType.String or SensorType.Version);

        public bool IsTableFormatSupported => Type is not SensorType.File;

        public bool IsDatapointFormatSupported => Type is SensorType.Integer or SensorType.Double or SensorType.Boolean
                                                  or SensorType.String or SensorType.TimeSpan;


        public SensorNodeViewModel(BaseSensorModel model) : base(model)
        {
            Update(model);
        }


        internal void Update(BaseSensorModel model)
        {
            base.Update(model);

            Type = model.Type;
            State = model.State;
            Integration = model.Integration;
            UpdateTime = model.LastUpdateTime;
            Status = model.Status.ToClient();
            ValidationError = State == SensorState.Muted ? GetMutedErrorTooltip(model.EndOfMuting) : model.Status?.Message;

            LastValue = model.LastValue;
            HasData = model.HasData;
            ShortStringValue = model.LastValue?.ShortInfo;

            FileNameString = GetFileNameString(model.Type, ShortStringValue);

            if (model is DoubleSensorModel or IntegerSensorModel or DoubleBarSensorModel or IntegerBarSensorModel)
                DataAlerts[Type] = model.DataPolicies.Select(p => BuildAlert(p, model)).ToList();

            AlertIcons.Clear();
            foreach (var alertIcon in model.PolicyResult.Icons)
                AlertIcons.TryAdd(alertIcon, 1);
        }

        private static DataAlertViewModel BuildAlert(Policy policy, BaseSensorModel sensor) => policy switch
        {
            IntegerDataPolicy p => new SingleDataAlertViewModel<IntegerValue, int>(p, sensor),
            DoubleDataPolicy p => new SingleDataAlertViewModel<DoubleValue, double>(p, sensor),
            IntegerBarDataPolicy p => new BarDataAlertViewModel<IntegerBarValue, int>(p, sensor),
            DoubleBarDataPolicy p => new BarDataAlertViewModel<DoubleBarValue, double>(p, sensor),
        };

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
