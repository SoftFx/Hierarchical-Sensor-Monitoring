using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Model;
using HSMServer.Helpers;
using HSMServer.Model.AccessKeysViewModels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.TreeViewModels
{
    public class ProductNodeViewModel : NodeViewModel
    {
        public string Id { get; }

        public string EncodedId { get; }

        public ConcurrentDictionary<string, ProductNodeViewModel> Nodes { get; } = new();

        public ConcurrentDictionary<Guid, SensorNodeViewModel> Sensors { get; } = new();

        public ConcurrentDictionary<Guid, AccessKeyViewModel> AccessKeys { get; } = new();

        public List<SensorNodeViewModel> FilteredSensors { get; internal set; } //r

        public bool IsAvailableForUser { get; internal set; } //r

        public bool IsAddingAccessKeysAvailable { get; internal set; } //r

        public int InnerFilteredSensorsCount { get; internal set; } //r

        public int SensorsWithNotificationsCount { get; internal set; } //r


        public ProductNodeViewModel(ProductModel model)
        {
            Id = model.Id;
            EncodedId = SensorPathHelper.Encode(Id);
            Name = model.DisplayName;
        }


        internal void Update(ProductModel model)
        {
            Name = model.DisplayName;
        }

        internal void AddSubNode(ProductNodeViewModel node)
        {
            Nodes.TryAdd(node.Id, node);
            node.Parent = this;
        }

        internal void AddSensor(SensorNodeViewModel sensor)
        {
            Sensors.TryAdd(sensor.Id, sensor);
            sensor.Parent = this;
        }

        internal void AddAccessKey(AccessKeyViewModel key) =>
            AccessKeys.TryAdd(key.Id, key);

        internal void UpdateAccessKeysAvailableOperations(bool isAccessKeysOperationsAvailable)
        {
            if (Nodes != null && !Nodes.IsEmpty)
                foreach (var (_, node) in Nodes)
                    node.UpdateAccessKeysAvailableOperations(isAccessKeysOperationsAvailable);

            IsAddingAccessKeysAvailable = isAccessKeysOperationsAvailable;

            foreach (var (_, accessKey) in AccessKeys)
                accessKey.IsChangeAvailable = isAccessKeysOperationsAvailable;
        }

        internal List<AccessKeyViewModel> GetAccessKeys() => AccessKeys.Values.ToList();
    }
}
