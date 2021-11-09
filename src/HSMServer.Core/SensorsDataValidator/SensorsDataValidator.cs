using HSMCommon.Constants;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Configuration;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Products;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using HSMServer.Core.Model;

namespace HSMServer.Core.SensorsDataValidator
{
    public class SensorsDataValidator : ISensorsDataValidator
    {
        private readonly IConfigurationProvider _configurationProvider;
        private readonly IDatabaseAdapter _databaseAdapter;
        private readonly IProductManager _productManager;
        private readonly ILogger<SensorsDataValidator> _logger;
        private int _maxPathLength;
        public SensorsDataValidator(IConfigurationProvider configurationProvider, IDatabaseAdapter databaseAdapter,
            IProductManager productManager, ILogger<SensorsDataValidator> logger)
        {
            _configurationProvider = configurationProvider;
            _databaseAdapter = databaseAdapter;
            _productManager = productManager;          
            _logger = logger;
            InitializeCommonParameters();
        }
       
        private void InitializeCommonParameters()
        {
            var pathLengthObject = _configurationProvider.ReadOrDefaultConfigurationObject(ConfigurationConstants.MaxPathLength);
            try
            {
                _maxPathLength = int.Parse(pathLengthObject.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to parse max path length!");
                throw;
            }           
        }

        public ValidationResult ValidateBoolean(bool value, string path, string productName, out string validationError)
        {
            var sensorInfo = _productManager.GetSensorInfo(productName, path);
            if (sensorInfo?.ValidationParameters == null || !sensorInfo.ValidationParameters.Any())
            {
                validationError = string.Empty;
                return ValidationResult.Ok;
            }
            foreach (var parameter in sensorInfo.ValidationParameters)
            {
                bool validateValue = bool.Parse(parameter.ValidationValue);
                if (validateValue == value)
                {
                    validationError = "Validation failed";
                    return ValidationResult.Failed;
                }
            }

            validationError = string.Empty;
            return ValidationResult.Ok;
        }

        public ValidationResult ValidateValueWithoutType(SensorValueBase value, string productName, out string validationError)
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
