using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace HSMServer.Core.Model
{
    [Flags]
    public enum ProductState : int
    {
        Disabled = 0,
        FullAccess = 1 << 30, // int: 2^31 - 1
    }


    public sealed class ProductModel : NodeBaseModel, INotificatable
    {
        public string Id { get; }

        public ProductState State { get; }

        public ConcurrentDictionary<Guid, AccessKeyModel> AccessKeys { get; }

        public ConcurrentDictionary<string, ProductModel> SubProducts { get; }

        public ConcurrentDictionary<Guid, BaseSensorModel> Sensors { get; }

        public ProductNotificationSettings Notifications { get; }


        string INotificatable.Name => DisplayName;

        NotificationSettings INotificatable.Notifications => Notifications;

        bool INotificatable.AreNotificationsEnabled(BaseSensorModel sensor) =>
            Notifications.Telegram.MessagesAreEnabled && sensor.ProductId == Id;


        public ProductModel()
        {
            AccessKeys = new ConcurrentDictionary<Guid, AccessKeyModel>();
            SubProducts = new ConcurrentDictionary<string, ProductModel>();
            Sensors = new ConcurrentDictionary<Guid, BaseSensorModel>();
            Notifications = new();
        }

        public ProductModel(ProductEntity entity) : this()
        {
            Id = entity.Id;
            AuthorId = Guid.TryParse(entity.AuthorId, out var authorId) ? authorId : null;
            State = (ProductState)entity.State;
            DisplayName = entity.DisplayName;
            Description = entity.Description;
            CreationDate = new DateTime(entity.CreationDate);
            Notifications = new(entity.NotificationSettings);
        }

        public ProductModel(string name) : this()
        {
            Id = Guid.NewGuid().ToString();
            State = ProductState.FullAccess;
            DisplayName = name;
            CreationDate = DateTime.UtcNow;
        }

        public ProductModel(string key, string name) : this(name)
        {
            Id = key;
        }

        internal bool AddAccessKey(AccessKeyModel key) => AccessKeys.TryAdd(key.Id, key);

        internal void AddSubProduct(ProductModel product)
        {
            product.ParentProduct = this;

            SubProducts.TryAdd(product.Id, product);
        }

        internal void AddSensor(BaseSensorModel sensor)
        {
            sensor.ParentProduct = this;

            Sensors.TryAdd(sensor.Id, sensor);
        }

        internal ProductEntity ToProductEntity() =>
            new()
            {
                Id = Id,
                AuthorId = AuthorId.ToString(),
                ParentProductId = ParentProduct?.Id,
                State = (int)State,
                DisplayName = DisplayName,
                Description = Description,
                CreationDate = CreationDate.Ticks,
                SubProductsIds = SubProducts.Select(p => p.Value.Id).ToList(),
                SensorsIds = Sensors.Select(p => p.Value.Id.ToString()).ToList(),
                NotificationSettings = Notifications.ToEntity(),
                Policies = GetPolicyIds(),
            };

        internal override void RemoveExpectedUpdateInterval()
        {
            UpdateChildSensorsValidationResult(this);

            base.RemoveExpectedUpdateInterval();
        }

        private static void UpdateChildSensorsValidationResult(ProductModel product)
        {
            foreach (var (_, sensor) in product.Sensors)
                if (sensor.ExpectedUpdateIntervalPolicy == null)
                    sensor.RemoveExpectedUpdateIntervalError();

            foreach (var (_, subProduct) in product.SubProducts)
                if (subProduct.ExpectedUpdateIntervalPolicy == null)
                    UpdateChildSensorsValidationResult(subProduct);
        }
    }
}
