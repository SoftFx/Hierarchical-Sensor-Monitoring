using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace HSMServer.Core.DataLayer
{
    public static class EntityConverter
    {
        public static ProductEntity Convert(string oldEntity)
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

            var authorId = Guid.Empty.ToString();
            var parentId = Guid.Empty.ToString();
            const int state = 0;
            var desc = string.Empty;
            var creationDate = DateTime.MinValue;
            var subProductsIds = new List<string>();
            var sensorsIds = new List<string>();
            const bool isConverted = true;

            name = rootElement.TryGetProperty("Name", out var jsonName) ? jsonName.GetString() : name;
            key = rootElement.TryGetProperty("Key", out var jsonKey) ? jsonKey.GetString() : key;
            rootElement.TryGetProperty("DateAdded", out var jsonAdded);
            dateAdded = DateTime.TryParse(jsonAdded.GetString(), out var parsedDate) ? parsedDate : dateAdded;

            var newEntity = new ProductEntity
            {
                Id = key,
                AuthorId = authorId,
                ParentProductId = parentId,
                State = state,
                DisplayName = name,
                Description = desc,
                CreationDate = creationDate.Ticks,
                DateAdded = dateAdded.Ticks,
                SubProductsIds = subProductsIds,
                SensorsIds = sensorsIds,
                IsConverted = isConverted
            };

            return newEntity;
        }
    }
}
