using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Helpers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
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

        public int AllSensorsCount { get; private set; }

        public List<SensorNodeViewModel> FilteredSensors { get; internal set; } //r

        public int InnerFilteredSensorsCount { get; internal set; } //r


        public ProductNodeViewModel(ProductModel model)
        {
            Id = model.Id;
            EncodedId = SensorPathHelper.Encode(Id);
            Name = model.DisplayName;
        }


        public bool IsChangingAccessKeysAvailable(User user) =>
            user.IsAdmin || ProductRoleHelper.IsManager(Id, user.ProductsRoles);


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

        internal List<AccessKeyViewModel> GetAccessKeys() => AccessKeys.Values.ToList();

        internal void UpdateCharacteristics()
        {
            int allSensorsCount = 0;

            if (Nodes != null && !Nodes.IsEmpty)
            {
                foreach (var (_, node) in Nodes)
                {
                    node.UpdateCharacteristics();

                    allSensorsCount += node.AllSensorsCount;
                }
            }

            AllSensorsCount = allSensorsCount + Sensors.Count;

            ModifyUpdateTime();
            ModifyStatus();
        }

        private void ModifyUpdateTime()
        {
            var sensorMaxTime = Sensors.Values.Count == 0 ? DateTime.MinValue : Sensors.Values.Max(x => x.UpdateTime);
            var nodeMaxTime = Nodes.Values.Count == 0 ? DateTime.MinValue : Nodes.Values.Max(x => x.UpdateTime);

            UpdateTime = sensorMaxTime > nodeMaxTime ? sensorMaxTime : nodeMaxTime;
        }

        private void ModifyStatus()
        {
            var statusFromSensors = Sensors.Values.Count == 0 ? SensorStatus.Ok : Sensors.Values.Max(s => s.Status);
            var statusFromNodes = Nodes.Values.Count == 0 ? SensorStatus.Ok : Nodes.Values.Max(n => n.Status);

            Status = statusFromNodes > statusFromSensors ? statusFromNodes : statusFromSensors;
        }
    }
}
