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

        public string EncodedId { get; }

        public ConcurrentDictionary<string, ProductNodeViewModel> Nodes { get; }

        public ConcurrentDictionary<Guid, SensorNodeViewModel> Sensors { get; }

        public ConcurrentDictionary<Guid, AccessKeyViewModel> AccessKeys { get; }

        public List<SensorNodeViewModel> VisibleSensors => Sensors.Values.Where(s => s.HasData).ToList();

        public int Count { get; private set; }

        public bool IsAvailableForUser { get; set; }


        public ProductNodeViewModel(ProductModel model)
        {
            Id = model.Id;
            EncodedId = SensorPathHelper.Encode(Id);
            Name = model.DisplayName;

            Nodes = new ConcurrentDictionary<string, ProductNodeViewModel>();
            Sensors = new ConcurrentDictionary<Guid, SensorNodeViewModel>();
            AccessKeys = new ConcurrentDictionary<Guid, AccessKeyViewModel>();

            foreach (var (_, sensor) in model.Sensors)
                AddSensor(new SensorNodeViewModel(sensor));

            foreach (var (id, key) in model.AccessKeys)
                AccessKeys.TryAdd(id, new AccessKeyViewModel(key));
        }


        internal void Update(ProductModel model)
        {
            Name = model.DisplayName;

            //TODO update sensors, subproducts and accessKeys
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

            Count = count + VisibleSensors.Count;

            ModifyUpdateTime();
            ModifyStatus();
        }

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
            var statusFromSensors = VisibleSensors.Count == 0 ? SensorStatus.Unknown : VisibleSensors.Max(s => s.Status);
            var statusFromNodes = Nodes.Values.Count == 0 ? SensorStatus.Unknown : Nodes.Values.Max(n => n.Status);

            Status = new List<SensorStatus> { statusFromNodes, statusFromSensors }.Max();
        }
    }
}
