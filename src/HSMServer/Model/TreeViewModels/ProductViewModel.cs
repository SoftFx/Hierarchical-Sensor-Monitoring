using HSMSensorDataObjects;
using HSMServer.Core.Cache.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.TreeViewModels
{
    public class ProductViewModel : NodeViewModel
    {
        public int Count { get; set; }

        public ConcurrentDictionary<Guid, ProductViewModel> Nodes { get; set; }

        public ConcurrentDictionary<Guid, SensorViewModel> Sensors { get; set; }


        public ProductViewModel(ProductModel model)
        {
            Id = model.Id;
            Name = model.DisplayName;

            Nodes = new ConcurrentDictionary<Guid, ProductViewModel>();
            Sensors = new ConcurrentDictionary<Guid, SensorViewModel>();

            foreach (var (_, sensor) in model.Sensors)
            {
                var sensorVM = new SensorViewModel(sensor, this);
                Sensors.TryAdd(sensorVM.Id, sensorVM);
            }
        }


        internal void Update(ProductModel model)
        {
            Name = model.DisplayName;

            foreach (var (sensorId, sensor) in model.Sensors)
                if (!Sensors.TryGetValue(sensorId, out var existingSensorVM))
                {
                    var sensorVM = new SensorViewModel(sensor, this);
                    Sensors.TryAdd(sensorVM.Id, sensorVM);
                }
                else
                    existingSensorVM.Update(sensor);
        }

        internal void AddSubNode(ProductViewModel node)
        {
            Nodes.TryAdd(node.Id, node);
            node.Parent = this;
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
