using System;
using System.Text.Json;
using HSMCommon.Constants;
using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Converters;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Products;
using HSMServer.Core.SensorsDataValidation;
using Microsoft.Extensions.Logging;

namespace HSMServer.Core.SensorsDataProcessor
{
    public class SensorsProcessor : ISensorsProcessor
    {
        private readonly ILogger<SensorsProcessor> _logger;
        private readonly IConverter _converter;
        private readonly ISensorsDataValidator _dataValidator;
        private readonly IProductManager _productManager;
        public SensorsProcessor(ILogger<SensorsProcessor> logger, IConverter converter, ISensorsDataValidator validator,
            IProductManager productManager)
        {
            _logger = logger;
            _converter = converter;
            _dataValidator = validator;
            _productManager = productManager;
        }

        #region Interface implementation

        public ValidationResult ProcessData(BoolSensorValue value, DateTime timeCollected, out SensorData processedData, out string processingError)
        {
            processedData = null;
            var baseProcessingResult = ProcessValueBase(value, out var productName, out processingError);
            if (baseProcessingResult == ValidationResult.Failed)
                return baseProcessingResult;

            var typedValidationResult = _dataValidator.ValidateBoolean(value.BoolValue, value.Path, productName,
            out var typedProcessingError);

            var worstResult = baseProcessingResult.GetWorst(typedValidationResult);
            processingError = CombineErrors(processingError, typedProcessingError);
            if (worstResult == ValidationResult.Failed)
                return worstResult;

            AddSensorIfNotRegistered(productName, value, out var transactionType);

            processedData = value.Convert(productName, timeCollected, transactionType);
            SetStatusViaValidationResult(processedData, worstResult);
            processedData.ValidationError = processingError;
            return worstResult;
        }
        public ValidationResult ProcessData(IntSensorValue value, DateTime timeCollected, out SensorData processedData,
            out string processingError)
        {
            processedData = null;
            var baseProcessingResult = ProcessValueBase(value, out var productName, out processingError);
            if (baseProcessingResult == ValidationResult.Failed)
                return baseProcessingResult;

            var typedValidationResult = _dataValidator.ValidateInteger(value.IntValue, value.Path, productName,
                out var typedProcessingError);

            var worstResult = baseProcessingResult.GetWorst(typedValidationResult);
            processingError = CombineErrors(processingError, typedProcessingError);
            if (worstResult == ValidationResult.Failed)
                return worstResult;

            AddSensorIfNotRegistered(productName, value, out var transactionType);

            processedData = value.Convert(productName, timeCollected, transactionType);
            SetStatusViaValidationResult(processedData, worstResult);
            processedData.ValidationError = processingError;
            return worstResult;
        }

        public ValidationResult ProcessData(DoubleSensorValue value, DateTime timeCollected, out SensorData processedData,
            out string processingError)
        {
            processedData = null;
            var baseProcessingResult = ProcessValueBase(value, out var productName, out processingError);
            if (baseProcessingResult == ValidationResult.Failed)
                return baseProcessingResult;

            var typedValidationResult = _dataValidator.ValidateDouble(value.DoubleValue, value.Path, productName,
                out var typedProcessingError);

            var worstResult = baseProcessingResult.GetWorst(typedValidationResult);
            processingError = CombineErrors(processingError, typedProcessingError);
            if (worstResult == ValidationResult.Failed)
                return worstResult;

            AddSensorIfNotRegistered(productName, value, out var transactionType);

            processedData = value.Convert(productName, timeCollected, transactionType);
            SetStatusViaValidationResult(processedData, worstResult);
            processedData.ValidationError = processingError;
            return worstResult;
        }

        public ValidationResult ProcessData(StringSensorValue value, DateTime timeCollected, out SensorData processedData,
            out string processingError)
        {
            processedData = null;
            var baseProcessingResult = ProcessValueBase(value, out var productName, out processingError);
            if (baseProcessingResult == ValidationResult.Failed)
                return baseProcessingResult;

            if (value.StringValue.Length > ValidationConstants.MAX_STRING_LENGTH)
            {
                baseProcessingResult = ValidationResult.OkWithError;
                processingError += ValidationConstants.SensorValueIsTooLong;
                value.StringValue = value.StringValue.Substring(0, ValidationConstants.MAX_STRING_LENGTH);
            }

            var typedValidationResult = _dataValidator.ValidateString(value.StringValue, value.Path, productName,
                out var typedProcessingError);

            var worstResult = baseProcessingResult.GetWorst(typedValidationResult);
            processingError = CombineErrors(processingError, typedProcessingError);
            if (worstResult == ValidationResult.Failed)
                return worstResult;

            AddSensorIfNotRegistered(productName, value, out var transactionType);

            processedData = value.Convert(productName, timeCollected, transactionType);
            SetStatusViaValidationResult(processedData, worstResult);
            processedData.ValidationError = processingError;
            return worstResult;
        }

        public ValidationResult ProcessData(DoubleBarSensorValue value, DateTime timeCollected, out SensorData processedData,
            out string processingError)
        {
            processedData = null;
            var baseProcessingResult = ProcessValueBase(value, out var productName, out processingError);
            if (baseProcessingResult == ValidationResult.Failed)
                return baseProcessingResult;

            var typedValidationResult = _dataValidator.ValidateDoubleBar(value.Max, value.Min, value.Mean, value.Count, value.Path,
                productName, out var typedProcessingError);

            var worstResult = baseProcessingResult.GetWorst(typedValidationResult);
            processingError = CombineErrors(processingError, typedProcessingError);
            if (worstResult == ValidationResult.Failed)
                return worstResult;

            AddSensorIfNotRegistered(productName, value, out var transactionType);

            processedData = value.Convert(productName, timeCollected, transactionType);
            SetStatusViaValidationResult(processedData, worstResult);
            processedData.ValidationError = processingError;
            return worstResult;
        }

        public ValidationResult ProcessData(IntBarSensorValue value, DateTime timeCollected, out SensorData processedData,
            out string processingError)
        {
            processedData = null;
            var baseProcessingResult = ProcessValueBase(value, out var productName, out processingError);
            if (baseProcessingResult == ValidationResult.Failed)
                return baseProcessingResult;

            var typedValidationResult = _dataValidator.ValidateIntBar(value.Max, value.Min, value.Mean, value.Count, value.Path,
                productName, out var typedProcessingError);

            var worstResult = baseProcessingResult.GetWorst(typedValidationResult);
            processingError = CombineErrors(processingError, typedProcessingError);
            if (worstResult == ValidationResult.Failed)
                return worstResult;

            AddSensorIfNotRegistered(productName, value, out var transactionType);

            processedData = value.Convert(productName, timeCollected, transactionType);
            SetStatusViaValidationResult(processedData, worstResult);
            processedData.ValidationError = processingError;
            return worstResult;
        }

        public ValidationResult ProcessData(FileSensorValue value, DateTime timeCollected, out SensorData processedData,
            out string processingError)
        {
            processedData = null;
            var baseProcessingResult = ProcessValueBase(value, out var productName, out processingError);
            if (baseProcessingResult == ValidationResult.Failed)
                return baseProcessingResult;

            AddSensorIfNotRegistered(productName, value, out var transactionType);
            processedData = value.Convert(productName, timeCollected, transactionType);
            SetStatusViaValidationResult(processedData, baseProcessingResult);
            processedData.ValidationError = processingError;
            return baseProcessingResult;
        }

        public ValidationResult ProcessData(FileSensorBytesValue value, DateTime timeCollected, out SensorData processedData,
            out string processingError)
        {
            processedData = null;
            var baseProcessingResult = ProcessValueBase(value, out var productName, out processingError);
            if (baseProcessingResult == ValidationResult.Failed)
                return baseProcessingResult;

            AddSensorIfNotRegistered(productName, value, out var transactionType);
            processedData = value.Convert(productName, timeCollected, transactionType);
            SetStatusViaValidationResult(processedData, baseProcessingResult);
            processedData.ValidationError = processingError;
            return baseProcessingResult;
        }

        public ValidationResult ProcessUnitedData(UnitedSensorValue unitedValue, DateTime timeCollected,
            out SensorData processedData, out string processingError)
        {
            processedData = null;
            var baseProcessingResult = ProcessValueBase(unitedValue, out var productName, out processingError);
            if (baseProcessingResult == ValidationResult.Failed)
                return baseProcessingResult;

            if (unitedValue.Data.Length > ValidationConstants.MAX_STRING_LENGTH)
            {
                baseProcessingResult = ValidationResult.OkWithError;
                processingError += ValidationConstants.SensorValueIsTooLong;
                unitedValue.Data = unitedValue.Data.Substring(0, ValidationConstants.MAX_STRING_LENGTH);
            }

            var typedValidationResult =
                ValidateUnitedValue(unitedValue.Data, unitedValue.Type, unitedValue.Path, productName,
                    out var typedError);
            var worstResult = baseProcessingResult.GetWorst(typedValidationResult);
            processingError = CombineErrors(processingError, typedError);
            if (worstResult == ValidationResult.Failed)
                return worstResult;

            AddSensorIfNotRegistered(productName, unitedValue, out var transactionType);

            processedData = unitedValue.Convert(productName, timeCollected, transactionType);
            SetStatusViaValidationResult(processedData, worstResult);
            processedData.ValidationError = processingError;
            return worstResult;
        }

        #endregion


        #region Common methods

        private ValidationResult ValidateUnitedValue(string data, SensorType sensorType, string path, string productName,
            out string validationError)
        {
            switch (sensorType)
            {
                case SensorType.BooleanSensor:
                {
                    bool boolValue = bool.Parse(data);
                    return _dataValidator.ValidateBoolean(boolValue, path, productName, out validationError);
                }
                case SensorType.IntSensor:
                {
                    int intValue = int.Parse(data);
                    return _dataValidator.ValidateInteger(intValue, path, productName, out validationError);
                }
                case SensorType.DoubleSensor:
                {
                    double doubleValue = double.Parse(data);
                    return _dataValidator.ValidateDouble(doubleValue, path, productName, out validationError);
                }
                case SensorType.StringSensor:
                {
                    return _dataValidator.ValidateString(data, path, productName, out validationError);
                }
                case SensorType.IntegerBarSensor:
                {
                    IntBarData intBarData = JsonSerializer.Deserialize<IntBarData>(data);
                    return _dataValidator.ValidateIntBar(intBarData.Max, intBarData.Min, intBarData.Mean,
                        intBarData.Count, path, productName, out validationError);
                }
                case SensorType.DoubleBarSensor:
                {
                    DoubleBarData doubleBarData = JsonSerializer.Deserialize<DoubleBarData>(data);
                    return _dataValidator.ValidateDoubleBar(doubleBarData.Max, doubleBarData.Min, doubleBarData.Mean,
                        doubleBarData.Count, path, productName, out validationError);
                }
                default:
                {
                    validationError = ValidationConstants.FailedToParseType;
                    return ValidationResult.Failed;
                }
            }
        }



        #endregion

        #region Typed validation

        #endregion

        #region Sub-methods

        /// <summary>
        /// Process sensor, set transaction type to TransactionType.Add if sensor is new; TransactionType.Update otherwise
        /// </summary>
        /// <param name="productName"></param>
        /// <param name="value"></param>
        /// <param name="transactionType"></param>
        private void AddSensorIfNotRegistered(string productName, SensorValueBase value,
            out TransactionType transactionType)
        {
            transactionType = TransactionType.Update;
            if (!_productManager.IsSensorRegistered(productName, value.Path))
            {
                _productManager.AddSensor(productName, value);
                transactionType = TransactionType.Add;
            }
        }

        private string CombineErrors(string commonError, string typedError)
        {
            bool isCommonErrorEmpty = string.IsNullOrEmpty(commonError);
            bool isTypedErrorEmpty = string.IsNullOrEmpty(typedError);

            if (isCommonErrorEmpty && isTypedErrorEmpty)
                return string.Empty;

            if (isCommonErrorEmpty)
                return typedError;

            if (isTypedErrorEmpty)
                return commonError;

            return $"{commonError}{Environment.NewLine}{typedError}";
        }

        /// <summary>
        /// The method performs common validation: check object for null & call Validator.ValidateValueWithoutType
        /// </summary>
        /// <param name="value"><see cref="SensorValueBase"/> object to validate</param>
        /// <param name="productName">The product name for current data object</param>
        /// <param name="processingError">Field for possible processing error</param>
        /// <returns>False in case of validation failure, true otherwise.</returns>
        private ValidationResult ProcessValueBase(SensorValueBase value, out string productName, out string processingError)
        {
            if (value == null)
            {
                productName = string.Empty;
                processingError = ValidationConstants.ObjectIsNull;
                return ValidationResult.Failed;
            }

            productName = _productManager.GetProductNameByKey(value.Key);
            return _dataValidator.ValidateValueWithoutType(value, productName, out processingError);
        }

        /// <summary>
        /// Change sensor status in case of difference between status from validation and current status
        /// </summary>
        /// <param name="sensorData"></param>
        /// <param name="validationResult"></param>
        private void SetStatusViaValidationResult(SensorData sensorData, ValidationResult validationResult)
        {
            var statusFromValidation = ConvertValidationResultToStatus(validationResult);
            sensorData.Status = statusFromValidation > sensorData.Status ? statusFromValidation : sensorData.Status;
        }

        private SensorStatus ConvertValidationResultToStatus(ValidationResult validationResult)
        {
            switch (validationResult)
            {
                case ValidationResult.Unknown:
                    return SensorStatus.Unknown;
                case ValidationResult.Ok:
                    return SensorStatus.Ok;
                case ValidationResult.OkWithError:
                    return SensorStatus.Warning;
                case ValidationResult.Failed:
                    return SensorStatus.Error;
                default:
                    throw new Exception($"Unknown validation result: {validationResult}");
            }
        }
        #endregion
    }
}