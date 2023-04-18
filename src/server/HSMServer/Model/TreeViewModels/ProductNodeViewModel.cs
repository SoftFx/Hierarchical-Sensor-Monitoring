using HSMCommon.Extensions;
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
            var temp = new Dictionary<SensorType, int>();
            
            if (Nodes != null && !Nodes.IsEmpty)
            {
                foreach (var (_, node) in Nodes)
                {
                    node.RecalculateCharacteristics();
                    allSensorsCount += node.AllSensorsCount;
                    foreach (var (_, sensor) in node.Sensors)
                    {
                        if (temp.TryGetValue(sensor.SensorType, out var _))
                            temp[sensor.SensorType]++;
                        else temp.TryAdd(sensor.SensorType, 1);
                    }
                }
            }
            
            foreach (var (_, sensor) in Sensors)
            {
                if (temp.TryGetValue(sensor.SensorType, out var _))
                {
                    temp[sensor.SensorType]++;
                }
                else temp.TryAdd(sensor.SensorType, 1);
            }

            TotalSensorsByType = temp;
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
