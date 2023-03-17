﻿using HSMServer.Core.Model;
using HSMServer.Extensions;
using System;

namespace HSMServer.Model.TreeViewModel
{
    public class SensorNodeViewModel : NodeViewModel
    {
        private const string ExtensionPattern = "Extension: ";
        private const string FileNamePattern = "File name: ";


        public SensorType SensorType { get; private set; }

        public SensorState State { get; private set; }

        public string ShortStringValue { get; private set; }

        public string FileNameString { get; private set; }

        public bool IsPlottingSupported { get; private set; }

        internal string Unit { get; private set; }

        internal BaseValue LastValue { get; private set; }

        public string ValidationError { get; private set; }

        public bool IsValidationErrorVisible => !string.IsNullOrEmpty(ValidationError);


        public SensorNodeViewModel(BaseSensorModel model) : base(model)
        {
            Update(model);
        }


        internal void Update(BaseSensorModel model)
        {
            base.Update(model);

            SensorType = model.Type;
            State = model.State;
            UpdateTime = model.LastUpdateTime;
            Status = model.Status.Status.ToClient();
            ValidationError = State == SensorState.Muted ? GetMutedErrorTooltip(model.EndOfMuting) : model.Status.Message;
            Unit = model.Unit;

            LastValue = model.LastValue;
            HasData = model.HasData;
            ShortStringValue = model.LastValue?.ShortInfo;

            IsPlottingSupported = IsSensorPlottingAvailable(model.Type);
            FileNameString = GetFileNameString(model.Type, ShortStringValue);
        }

        private static bool IsSensorPlottingAvailable(SensorType type) => type is not SensorType.String;

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
