using HSMSensorDataObjects;
using HSMServer.Core.Cache.Entities;
using HSMServer.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.TreeViewModels
{
    public class ProductNodeViewModel : NodeViewModel
    {
        public string Id { get; }

        public string EncodedId => SensorPathHelper.Encode(Id);

        public ConcurrentDictionary<string, ProductNodeViewModel> Nodes { get; }

        public ConcurrentDictionary<Guid, SensorNodeViewModel> Sensors { get; }

        public int Count { get; private set; }

        public bool IsAvailableForUser { get; set; }


        public ProductNodeViewModel(ProductModel model)
        {
            Id = model.Id;
            Name = model.DisplayName;

            Nodes = new ConcurrentDictionary<string, ProductNodeViewModel>();
            Sensors = new ConcurrentDictionary<Guid, SensorNodeViewModel>();

            foreach (var (_, sensor) in model.Sensors)
            {
                var sensorVM = new SensorNodeViewModel(sensor);
                AddSensor(sensorVM);
            }
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

            Count = count + (Sensors?.Count ?? 0);

            ModifyUpdateTime();
            ModifyStatus();
        }

        private void ModifyUpdateTime()
        {
            var sensorMaxTime = (Sensors?.Values?.Count ?? 0) == 0 ? null : Sensors?.Values.Max(x => x.UpdateTime);
            var nodeMaxTime = (Nodes?.Values?.Count ?? 0) == 0 ? null : Nodes?.Values.Max(x => x.UpdateTime);

            if (sensorMaxTime.HasValue && nodeMaxTime.HasValue)
                UpdateTime = new List<DateTime> { sensorMaxTime.Value, nodeMaxTime.Value }.Max();
            else if (sensorMaxTime.HasValue)
                UpdateTime = sensorMaxTime.Value;
            else if (nodeMaxTime.HasValue)
                UpdateTime = nodeMaxTime.Value;
        }

        private void ModifyStatus()
        {
            var statusFromSensors = (Sensors?.Values?.Count ?? 0) == 0 ? SensorStatus.Unknown : Sensors.Values.Max(s => s.Status);
            var statusFromNodes = (Nodes?.Values?.Count ?? 0) == 0 ? SensorStatus.Unknown : Nodes.Values.Max(n => n.Status);

            Status = new List<SensorStatus> { statusFromNodes, statusFromSensors }.Max();
        }
    }
}
