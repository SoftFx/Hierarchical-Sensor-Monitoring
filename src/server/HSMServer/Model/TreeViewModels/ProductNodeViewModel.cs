﻿using HSMCommon.Extensions;
using HSMServer.Core.Model;
using HSMServer.Helpers;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.Authentication;
using HSMServer.Notification.Settings;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.TreeViewModel
{
    public class ProductNodeViewModel : NodeViewModel, INotificatable
    {
        public ConcurrentDictionary<Guid, ProductNodeViewModel> Nodes { get; } = new();

        public ConcurrentDictionary<Guid, SensorNodeViewModel> Sensors { get; } = new();

        public ConcurrentDictionary<Guid, AccessKeyViewModel> AccessKeys { get; } = new();

        public Dictionary<SensorType, int> TotalSensorsByType { get; set; } = new();
        
        public Dictionary<SensorStatus, int> TotalSensorsByStatuses { get; set; } = new();


        public NotificationSettings Notifications { get; }


        public int AllSensorsCount { get; private set; }


        public override bool HasData =>
    Sensors.Values.Any(s => s.HasData) || Nodes.Values.Any(n => n.HasData);

        public bool IsEmpty => AllSensorsCount == 0;


        public ProductNodeViewModel(ProductModel model) : base(model)
        {
            Notifications = new(model.NotificationsSettings);

            Update(model);
        }


        public bool IsChangingAccessKeysAvailable(User user) =>
            user.IsAdmin || ProductRoleHelper.IsManager(Id, user.ProductsRoles);

        internal void AddSubNode(ProductNodeViewModel node)
        {
            node.Parent = this;
            Nodes.TryAdd(node.Id, node);
        }

        internal void AddSensor(SensorNodeViewModel sensor)
        {
            sensor.Parent = this;
            Sensors.TryAdd(sensor.Id, sensor);
        }

        internal void AddAccessKey(AccessKeyViewModel key) => AccessKeys.TryAdd(key.Id, key);

        internal List<AccessKeyViewModel> GetAccessKeys() => AccessKeys.Values.ToList();

        internal ProductNodeViewModel RecalculateCharacteristics()
        {
            int allSensorsCount = 0;
            var types = new Dictionary<SensorType, int>();
            var statuses = new Dictionary<SensorStatus, int>();
            
            if (Nodes != null && !Nodes.IsEmpty)
            {
                foreach (var (_, node) in Nodes)
                {
                    node.RecalculateCharacteristics();
                    allSensorsCount += node.AllSensorsCount;

                    foreach (var (type, sensor) in node.TotalSensorsByType)
                    {
                        if (types.TryGetValue(type, out var _))
                            types[type] += sensor;
                        else types.TryAdd(type, sensor);
                    }
                    
                    foreach (var (status, sensor) in node.TotalSensorsByStatuses)
                    {
                        if (statuses.TryGetValue(status, out var _))
                            statuses[status] += sensor;
                        else statuses.TryAdd(status, sensor);
                    }
                    
                }
            }
            
            foreach (var (_, sensor) in Sensors)
            {
                if (types.TryGetValue(sensor.Type, out var _))
                    types[sensor.Type]++;
                else types.TryAdd(sensor.Type, 1);
                
                if (statuses.TryGetValue(sensor.Status, out var _))
                    statuses[sensor.Status]++;
                else statuses.TryAdd(sensor.Status, 1);
            }

            TotalSensorsByType = types;
            TotalSensorsByStatuses = statuses;
            
            AllSensorsCount = allSensorsCount + Sensors.Count;
            
            ModifyUpdateTime();
            ModifyStatus();

            return this;
        }

        private void ModifyUpdateTime()
        {
            var sensorMaxTime = Sensors.Values.MaxOrDefault(x => x.UpdateTime);
            var nodeMaxTime = Nodes.Values.MaxOrDefault(x => x.UpdateTime);

            UpdateTime = sensorMaxTime > nodeMaxTime ? sensorMaxTime : nodeMaxTime;
        }

        private void ModifyStatus()
        {
            var nodesStatus = Sensors.Values.MaxOrDefault(s => s.Status);
            var sensorStatus = Nodes.Values.MaxOrDefault(n => n.Status);

            Status = sensorStatus > nodesStatus ? sensorStatus : nodesStatus;
        }
    }
}
