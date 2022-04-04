using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.TreeValuesCache.Entities
{
    public class SensorModel
    {
        public Guid Id { get; }
        public string Path { get; } // если будет логика создания недостоющих продуктов, Path не нужен
        public ProductModel ParentProduct { get; set; }
        public string SensorName { get; }
        public string Description { get; }
        public SensorType SensorType { get; }
        public TimeSpan ExpectedUpdateInterval { get; }
        public DateTime LastUpdateTime { get; }
        public SensorStatus Status { get; }
        // поля для хранения значений сенсоров? может хранить SensorValueBase/SensorValueBaseData? и из него уже на View составлять строки для отображения
        // или создать сенсоры-наследники для каждого типа сенсора
        public string TypedData { get; }
        public int OriginalFileSensorContentSize { get; }


        public SensorModel(SensorEntity entity, SensorDataEntity dataEntity)
        {
            Id = new Guid(entity.Id);
            Path = entity.Path;
            SensorName = GetSensorName(entity.Path);
            Description = entity.Description;
            SensorType = (SensorType)entity.SensorType;
            ExpectedUpdateInterval = new TimeSpan(entity.ExpectedUpdateIntervalTicks);

            // sorting entities in order from newest to oldest
            //dataEntities.Sort((entity1, entity2) => entity2.Time.CompareTo(entity1.Time));
            //var newestDataEntity = dataEntities[0];

            LastUpdateTime = dataEntity.TimeCollected;
            Status = (SensorStatus)dataEntity.Status;
            OriginalFileSensorContentSize = dataEntity.OriginalFileSensorContentSize;

            TypedData = dataEntity.TypedData;
        }


        private static string GetSensorName(string path) => path?.Split(CommonConstants.SensorPathSeparator)?[^1];
    }
}
