using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Converters;
using HSMServer.Core.Helpers;
using HSMServer.Core.SensorsDataValidation;
using System;

namespace HSMServer.Core.Cache.Entities
{
    public sealed class SensorModel
    {
        public Guid Id { get; }

        public string SensorName { get; }

        public string ProductName { get; }

        public string Path { get; }

        public string Description { get; private set; }

        public TimeSpan ExpectedUpdateInterval { get; private set; }

        public string Unit { get; private set; }

        public SensorType SensorType { get; private set; }

        public DateTime SensorTime { get; private set; }

        public DateTime LastUpdateTime { get; private set; }

        public SensorStatus Status { get; private set; }

        public string TypedData { get; private set; } // TODO: хранить последние 100 значений, классы-наследники??

        public int OriginalFileSensorContentSize { get; private set; }

        public string ValidationError { get; private set; }

        public ProductModel ParentProduct { get; private set; }


        internal SensorModel(SensorEntity entity, SensorDataEntity dataEntity)
        {
            Id = Guid.Parse(entity.Id);
            SensorName = entity.DisplayName;
            Description = entity.Description;
            SensorType = (SensorType)entity.Type;
            ExpectedUpdateInterval = new TimeSpan(entity.ExpectedUpdateIntervalTicks);
            Unit = entity.Unit;

            if (dataEntity != null)
            {
                // sorting entities in order from newest to oldest
                //dataEntities.Sort((entity1, entity2) => entity2.Time.CompareTo(entity1.Time));
                //var newestDataEntity = dataEntities[0];

                SensorTime = dataEntity.Time;
                LastUpdateTime = dataEntity.TimeCollected;
                Status = (SensorStatus)dataEntity.Status;
                OriginalFileSensorContentSize = dataEntity.OriginalFileSensorContentSize;

                TypedData = dataEntity.TypedData;
            }
        }

        //internal SensorModel(SensorValueBase sensorValue, string productName,
        //    DateTime timeCollected, ValidationResult validationResult)
        //{
        //    Id = Guid.NewGuid();
        //    SensorName = GetSensorName(sensorValue.Path);
        //    ProductName = productName;
        //    Path = sensorValue.Path;

        //    UpdateData(sensorValue, timeCollected, validationResult);
        //}


        internal void AddParent(ProductModel product) => ParentProduct = product;

        internal void Update(SensorUpdate sensor)
        {
            Description = sensor.Description;
            ExpectedUpdateInterval = TimeSpan.Parse(sensor.ExpectedUpdateInterval);
            Unit = sensor.Unit;
        }

        //internal void UpdateData(SensorValueBase sensorValue, DateTime timeCollected, ValidationResult validationResult)
        //{
        //    if (sensorValue is FileSensorBytesValue fileSensor)
        //    {
        //        OriginalFileSensorContentSize = fileSensor.FileContent.Length;
        //        sensorValue = fileSensor.CompressContent();
        //    }

        //    Description = sensorValue.Description;
        //    SensorType = SensorTypeFactory.GetSensorType(sensorValue);
        //    TypedData = TypedDataFactory.GetTypedData(sensorValue);
        //    SensorTime = sensorValue.Time;
        //    LastUpdateTime = timeCollected;
        //    ValidationError = validationResult.Error;

        //    var sensorStatus = GetSensorStatus(validationResult);
        //    Status = sensorStatus > sensorValue.Status ? sensorStatus : sensorValue.Status;
        //}

        internal bool IsSensorMetadataUpdated(SensorValueBase sensorValue) =>
            sensorValue.Description != Description || SensorTypeFactory.GetSensorType(sensorValue) != SensorType;

        internal void ClearData()
        {
            SensorTime = DateTime.MinValue;
            LastUpdateTime = DateTime.MinValue;
            Status = SensorStatus.Unknown;
            OriginalFileSensorContentSize = 0;
            TypedData = null;
        }

        internal SensorEntity ToSensorEntity() =>
            new()
            {
                Id = Id.ToString(),
                ProductId = ParentProduct?.Id,
                DisplayName = SensorName,
                Description = Description,
                Type = (byte)SensorType,
                ExpectedUpdateIntervalTicks = ExpectedUpdateInterval.Ticks,
                Unit = Unit,
            };

        internal SensorDataEntity ToSensorDataEntity() =>
            new()
            {
                Status = (byte)Status,
                Path = Path,
                Time = SensorTime.ToUniversalTime(),
                TimeCollected = LastUpdateTime.ToUniversalTime(),
                Timestamp = SensorValuesExtensions.GetTimestamp(SensorTime),
                TypedData = TypedData,
                DataType = (byte)SensorType,
                OriginalFileSensorContentSize = OriginalFileSensorContentSize,
            };


        private static string GetSensorName(string path) => path?.Split(CommonConstants.SensorPathSeparator)?[^1];

        private static SensorStatus GetSensorStatus(ValidationResult validationResult) =>
            validationResult.ResultType switch
            {
                ResultType.Unknown => SensorStatus.Unknown,
                ResultType.Ok => SensorStatus.Ok,
                ResultType.Warning => SensorStatus.Warning,
                ResultType.Error => SensorStatus.Error,
                _ => throw new InvalidCastException($"Unknown validation result: {validationResult.ResultType}"),
            };
    }
}
