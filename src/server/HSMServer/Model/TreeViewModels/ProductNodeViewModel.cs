using HSMCommon.Constants;
using HSMCommon.Extensions;
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

        public TelegramSettingsViewModel TelegramSettings { get; }

        public int AllSensorsCount { get; private set; }

        public TimeIntervalViewModel ExpectedUpdateInterval { get; set; } = new();


        public ProductNodeViewModel(ProductModel model)
        {
            Id = model.Id;
            EncodedId = SensorPathHelper.Encode(Id);
            Name = model.DisplayName;
            Path = CommonConstants.SensorPathSeparator.ToString();
            TelegramSettings = new(model.Notifications.Telegram);
        }


        public bool IsChangingAccessKeysAvailable(User user) =>
            user.IsAdmin || ProductRoleHelper.IsManager(Id, user.ProductsRoles);

        public string GetSensorsCountString(NodeStateViewModel nodeState)
        {
            var sensorsCount = nodeState.FilteredSensorsCount == AllSensorsCount
                ? $"{AllSensorsCount}"
                : $"{nodeState.FilteredSensorsCount}/{AllSensorsCount}";

            return $"({sensorsCount} sensors)";
        }


        internal void Update(ProductModel model)
        {
            Name = model.DisplayName;
            TelegramSettings.Update(model.Notifications.Telegram);
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

        internal List<AccessKeyViewModel> GetEditProductAccessKeys()
        {
            var accessKeys = GetAccessKeys().Select(k => k.Copy()).ToList();
            accessKeys.ForEach(k => k.HasProductColumn = false);

            return accessKeys;
        }


        internal void RecalculateCharacteristics()
        {
            int allSensorsCount = 0;

            if (Nodes != null && !Nodes.IsEmpty)
            {
                foreach (var (_, node) in Nodes)
                {
                    node.Path = $"{node.Parent.Path}{node.Name}{CommonConstants.SensorPathSeparator}";
                    node.RecalculateCharacteristics();

                    allSensorsCount += node.AllSensorsCount;
                }
            }

            AllSensorsCount = allSensorsCount + Sensors.Count;

            ModifyUpdateTime();
            ModifyStatus();
        }

        private void ModifyUpdateTime()
        {
            var sensorMaxTime = Sensors.Values.MaxOrDefault(x => x.UpdateTime);
            var nodeMaxTime = Nodes.Values.MaxOrDefault(x => x.UpdateTime);

            UpdateTime = sensorMaxTime > nodeMaxTime ? sensorMaxTime : nodeMaxTime;
        }

        private void ModifyStatus()
        {
            var statusFromSensors = Sensors.Values.MaxOrDefault(s => s.Status);
            var statusFromNodes = Nodes.Values.MaxOrDefault(n => n.Status);

            Status = statusFromNodes > statusFromSensors ? statusFromNodes : statusFromSensors;
        }
    }
}
