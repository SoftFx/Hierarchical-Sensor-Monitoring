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

        public List<SensorNodeViewModel> VisibleSensors => Sensors.Values.Where(s => s.HasData).ToList();

        public bool IsAvailableForUser { get; internal set; }

        public bool IsAddingAccessKeysAvailable { get; internal set; }

        public int Count { get; private set; }


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

        internal void Recursion()
        {
            int count = 0;
            if (Nodes != null && !Nodes.IsEmpty)
            {
                foreach (var (_, node) in Nodes)
                {
                    node.Recursion();
                    count += node.Count;
                }
            }

            Count = count + VisibleSensors.Count;

            ModifyUpdateTime();
            ModifyStatus();
        }

        internal void UpdateAccessKeysAvailableOperations(bool isAccessKeysOperationsAvailable)
        {
            if (Nodes != null && !Nodes.IsEmpty)
                foreach (var (_, node) in Nodes)
                    node.UpdateAccessKeysAvailableOperations(isAccessKeysOperationsAvailable);

            IsAddingAccessKeysAvailable = isAccessKeysOperationsAvailable;

            foreach (var (_, accessKey) in AccessKeys)
                accessKey.IsChangeAvailable = isAccessKeysOperationsAvailable;
        }

        internal void UpdateNotificationsStatus() =>
            IsNotificationsEnabled = Sensors.Any(s => s.Value.IsNotificationsEnabled) ||
                                     Nodes.Any(n => n.Value.IsNotificationsEnabled);

        internal List<AccessKeyViewModel> GetAccessKeys() => AccessKeys.Values.ToList();

        private void ModifyUpdateTime()
        {
            var sensorMaxTime = VisibleSensors.Count == 0 ? null : VisibleSensors?.Max(x => x.UpdateTime);
            var nodeMaxTime = Nodes.Values.Count == 0 ? null : Nodes?.Values.Max(x => x.UpdateTime);

            if (sensorMaxTime.HasValue && nodeMaxTime.HasValue)
                UpdateTime = new List<DateTime> { sensorMaxTime.Value, nodeMaxTime.Value }.Max();
            else if (sensorMaxTime.HasValue)
                UpdateTime = sensorMaxTime.Value;
            else if (nodeMaxTime.HasValue)
                UpdateTime = nodeMaxTime.Value;
        }

        private void ModifyStatus()
        {
            var statusFromSensors = VisibleSensors.Count == 0 ? SensorStatus.Ok : VisibleSensors.Max(s => s.Status);
            var statusFromNodes = Nodes.Values.Count == 0 ? SensorStatus.Ok : Nodes.Values.Max(n => n.Status);

            Status = new List<SensorStatus> { statusFromNodes, statusFromSensors }.Max();
        }
    }
}
