using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.Entities;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace HSMServer.Core.DataLayer
{
    public static class EntityConverter
    {
        public static ProductEntity ConvertProductEntity(string oldEntity)
        {
            var jsonDocument = JsonDocument.Parse(oldEntity);
            var rootElement = jsonDocument.RootElement;

            if (rootElement.TryGetProperty("Id", out _))
            {
                return JsonSerializer.Deserialize<ProductEntity>(oldEntity);
            }

            var name = string.Empty;
            var key = Guid.NewGuid().ToString();
            var dateAdded = DateTime.MinValue;

            name = rootElement.TryGetProperty("Name", out var jsonName) ? jsonName.GetString() : name;
            key = rootElement.TryGetProperty("Key", out var jsonKey) ? jsonKey.GetString() : key;
            dateAdded = rootElement.TryGetProperty("DateAdded", out var jsonAdded) ? 
                jsonAdded.GetDateTime() : dateAdded;

            var newEntity = new ProductEntity
            {
                Id = key,
                AuthorId = string.Empty,
                ParentProductId = string.Empty,
                State = (int)ProductState.FullAccess,
                DisplayName = name,
                Description = string.Empty,
                CreationDate = dateAdded.Ticks,
                SubProductsIds = new List<string>(),
                SensorsIds = new List<string>(),
                IsConverted = true
            };

            return newEntity;
        }

        //public static SensorEntity ConvertSensorEntity(string oldEntity)
        //{
        //    var jsonDocument = JsonDocument.Parse(oldEntity);
        //    var rootElement = jsonDocument.RootElement;

        //    if (rootElement.TryGetProperty("Id", out _))
        //    {
        //        return JsonSerializer.Deserialize<SensorEntity>(oldEntity);
        //    }

        //    var id = Guid.NewGuid().ToString();
        //    var productId = string.Empty;
        //    var path = string.Empty;
        //    var productName = string.Empty;
        //    var sensorName = string.Empty;
        //    var desc = string.Empty;
        //    var sensorType = 0;
        //    long ticksInterval = 0;
        //    var unit = string.Empty;
        //    var validationParameters = new List<ValidationParameterEntity>();
        //    const bool isConverted = true;

        //    path = rootElement.TryGetProperty("Path", out var jsonPath) ? jsonPath.GetString() : path;
        //    productName = rootElement.TryGetProperty("ProductName", out var jsonProductName) 
        //        ? jsonProductName.GetString() : productName;
        //    sensorName = rootElement.TryGetProperty("SensorName", out var jsonSensorName) 
        //        ? jsonSensorName.GetString() : sensorName;
        //    desc = rootElement.TryGetProperty("Description", out var jsonDesc) ? jsonDesc.GetString() : desc;
        //    sensorType = rootElement.TryGetProperty("SensorType", out var jsonSensorType) ?
        //        jsonSensorType.GetInt32() : sensorType;
        //    ticksInterval = rootElement.TryGetProperty("ExpectedUpdateIntervalTicks", out var jsonTicks) ?
        //        jsonTicks.GetInt64() : ticksInterval;
        //    unit = rootElement.TryGetProperty("Unit", out var jsonUnit) ? jsonUnit.GetString() : unit;

        //    rootElement.TryGetProperty("ValidationParameters", out var jsonParameters);
        //    if (jsonParameters.GetArrayLength() > 0)
        //    {
        //        foreach (var parameter in jsonParameters.EnumerateArray())
        //        {
        //            validationParameters.Add(ConvertValidationParameter(parameter));
        //        }
        //    }
            
        //    var newEntity = new SensorEntity()
        //    {
        //        Id = id,
        //        ProductId = productId,
        //        Path = path,
        //        ProductName = productName,
        //        SensorName = sensorName,
        //        Description = desc,
        //        SensorType = sensorType,
        //        ExpectedUpdateIntervalTicks = ticksInterval,
        //        Unit = unit,
        //        ValidationParameters = validationParameters,
        //        IsConverted = isConverted
        //    };

        //    return newEntity;
        //}

        //public static ValidationParameterEntity ConvertValidationParameter(JsonElement parameter)
        //{
        //    var entity = new ValidationParameterEntity
        //    {
        //        ParameterType = parameter.TryGetProperty("ParameterType", out var jsonParameterType)
        //                    ? jsonParameterType.GetInt32() : 0,
        //        ValidationValue = parameter.TryGetProperty("ValidationValue", out var jsonValidationValue)
        //                    ? jsonValidationValue.GetString() : string.Empty
        //    };

        //    return entity;
        //}
    }
}
