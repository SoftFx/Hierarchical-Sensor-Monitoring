using HSMCommon.Constants;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Configuration;
using HSMServer.Core.Model;
using Microsoft.Extensions.Logging;
using System;

namespace HSMServer.Core.SensorsDataValidation
{
    public class SensorsDataValidator : ISensorsDataValidator
    {
        private readonly IConfigurationProvider _configurationProvider;
        private readonly ILogger<SensorsDataValidator> _logger;
        private int _maxPathLength = 10;
        public SensorsDataValidator(IConfigurationProvider configurationProvider, ILogger<SensorsDataValidator> logger)
        {
            _configurationProvider = configurationProvider;
            _logger = logger;
        }


        public ValidationResult ValidateBoolean(bool value, string path, string productName, out string validationError)
        {
            validationError = string.Empty;
            return ValidationResult.Ok;
        }

        public ValidationResult ValidateValueWithoutType(SensorValueBase value, out string validationError)
        {
            var count = value.Path.Split('/').Length;
            if (count > _maxPathLength)
            {
                validationError = ValidationConstants.PathTooLong;
                return ValidationResult.Failed;
            }
            validationError = string.Empty;
            return ValidationResult.Ok;
        }

        public ValidationResult ValidateInteger(int value, string path, string productName, out string validationError)
        {
            validationError = string.Empty;
            return ValidationResult.Ok;
        }

        public ValidationResult ValidateDouble(double value, string path, string productName, out string validationError)
        {
            validationError = string.Empty;
            return ValidationResult.Ok;
        }

        public ValidationResult ValidateString(string value, string path, string productName, out string validationError)
        {
            validationError = string.Empty;
            return ValidationResult.Ok;
        }

        public ValidationResult ValidateIntBar(int max, int min, int mean, int count, string path, string productName, out string validationError)
        {
            validationError = string.Empty;
            return ValidationResult.Ok;
        }

        public ValidationResult ValidateDoubleBar(double max, double min, double mean, int count, string path, string productName, out string validationError)
        {
            validationError = string.Empty;
            return ValidationResult.Ok;
        }
    }
}
