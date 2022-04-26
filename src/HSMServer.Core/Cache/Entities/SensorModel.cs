using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Converters;
using HSMServer.Core.SensorsDataValidation;
using System;

namespace HSMServer.Core.Cache.Entities
{
    public sealed class SensorModel
    {
        public Guid Id { get; }

        public string SensorName { get; }

        public TimeSpan ExpectedUpdateInterval { get; }

        public string ProductName { get; }

        public string Path { get; }

        public string Description { get; private set; }

        public SensorType SensorType { get; private set; }

        public DateTime SensorTime { get; private set; }

        public DateTime LastUpdateTime { get; private set; }

        public SensorStatus Status { get; private set; }

        public string TypedData { get; private set; } // TODO: хранить последние 100 значений, классы-наследники??

        public int OriginalFileSensorContentSize { get; private set; }

        public string ValidationError { get; private set; }

        public ProductModel ParentProduct { get; set; }


        internal SensorModel(SensorEntity entity, SensorDataEntity dataEntity)
        {
            Id = Guid.Parse(entity.Id);
            SensorName = string.IsNullOrEmpty(entity.SensorName) ? GetSensorName(entity.Path) : entity.SensorName; // TODO: is sensorName right in database????
            Description = entity.Description;
            SensorType = (SensorType)entity.SensorType;
            ExpectedUpdateInterval = new TimeSpan(entity.ExpectedUpdateIntervalTicks);
            ProductName = entity.ProductName;
            Path = entity.Path;

            if (dataEntity != null)
            {
                // sorting entities in order from newest to oldest
                //dataEntities.Sort((entity1, entity2) => entity2.Time.CompareTo(entity1.Time));
                //var newestDataEntity = dataEntities[0];

                LastUpdateTime = dataEntity.TimeCollected;
                Status = (SensorStatus)dataEntity.Status;
                OriginalFileSensorContentSize = dataEntity.OriginalFileSensorContentSize;

                TypedData = dataEntity.TypedData;
            }
        }

        internal SensorModel(SensorValueBase sensorValue, DateTime timeCollected, ValidationResult validationResult)
        {
            Id = Guid.NewGuid();
            SensorName = GetSensorName(sensorValue.Path);
            Path = sensorValue.Path;

            UpdateData(sensorValue, timeCollected, validationResult);
        }


        internal void UpdateData(SensorValueBase sensorValue, DateTime timeCollected, ValidationResult validationResult)
        {
            Description = sensorValue.Description;
            SensorType = SensorTypeFactory.GetSensorType(sensorValue);
            TypedData = TypedDataFactory.GetTypedData(sensorValue);
            SensorTime = sensorValue.Time;
            LastUpdateTime = timeCollected;
            ValidationError = validationResult.Error;

            var sensorStatus = GetSensorStatus(validationResult);
            Status = sensorStatus > sensorValue.Status ? sensorStatus : sensorValue.Status;
        }

        internal void ClearData()
        {
            LastUpdateTime = DateTime.MinValue;
            Status = SensorStatus.Unknown;
            OriginalFileSensorContentSize = 0;
            TypedData = null;
        }

        internal SensorEntity ToSensorEntity() =>
            new()
            {
                Id = Id.ToString(),
                ProductId = ParentProduct.Id,
                SensorName = SensorName,
                Description = Description,
                SensorType = (int)SensorType,
                ExpectedUpdateIntervalTicks = ExpectedUpdateInterval.Ticks,
                ProductName = ProductName,
                Path = Path,
                //Unit ???
                //ValidationParameters ???
            };

        internal SensorDataEntity ToSensorDataEntity() =>
            new()
            {
                Id = Id.ToString(),
                Status = (byte)Status,
                Path = Path,
                Time = SensorTime.ToUniversalTime(),
                TimeCollected = LastUpdateTime.ToUniversalTime(),
                Timestamp = GetTimestamp(SensorTime),
                TypedData = TypedData,
                DataType = (byte)SensorType,
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

        private static long GetTimestamp(DateTime dateTime)
        {
            var timeSpan = dateTime - DateTime.UnixEpoch;
            return (long)timeSpan.TotalSeconds;
        }
    }
}
