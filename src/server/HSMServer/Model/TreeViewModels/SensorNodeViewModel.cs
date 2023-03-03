using HSMServer.Core.Model;
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


        public SensorNodeViewModel(BaseSensorModel model) : base(model.Id)
        {
            Update(model);
        }


        internal void Update(BaseSensorModel model)
        {
            base.Update(model);

            SensorType = model.Type;
            State = model.State;
            UpdateTime = model.LastUpdateTime;
            Status = model.ValidationResult.Result.ToClient();
            ValidationError = State == SensorState.Ignored ? GetIgnoredErrorTooltip(model.EndOfIgnore) : model.ValidationResult.Message;
            Path = model.Path;
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

        private static string GetIgnoredErrorTooltip(DateTime? endOfIgnore) =>
            endOfIgnore is not null && endOfIgnore != DateTime.MaxValue
                ? $"Ignore until {endOfIgnore.Value.ToDefaultFormat()}"
                : $"Ignored forever";
    }
}
