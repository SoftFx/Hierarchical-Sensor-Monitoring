using HSMSensorDataObjects;
using HSMServer.Core.TreeValuesCache.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Model.TreeViewModels
{
    public class ProductViewModel : NodeViewModel
    {
        public int Count { get; set; }
        public NodeViewModel Parent { get; set; }
        public ConcurrentDictionary<string, ProductViewModel> Nodes { get; set; }
        public ConcurrentDictionary<string, SensorViewModel> Sensors { get; set; }


        public ProductViewModel(ProductModel model)
        {
            Id = model.Id.ToString();
            Name = model.DisplayName;

            Nodes = new ConcurrentDictionary<string, ProductViewModel>();
            Sensors = new ConcurrentDictionary<string, SensorViewModel>();

            foreach (var (_, sensor) in model.Sensors)
            {
                var sensorVM = new SensorViewModel(sensor);
                Sensors.TryAdd(sensorVM.Id, sensorVM); // TODO: key is id or path?
            }
        }


        public void AddSubNode(ProductViewModel node)
        {
            Nodes.TryAdd(node.Id, node);
            node.Parent = this;
        }

        public void Recursion()
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

        public void ModifyUpdateTime()
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

        public void ModifyStatus()
        {
            var statusFromSensors = (Sensors?.Values?.Count ?? 0) == 0 ? SensorStatus.Unknown : Sensors.Values.Max(s => s.Status);
            var statusFromNodes = (Nodes?.Values?.Count ?? 0) == 0 ? SensorStatus.Unknown : Nodes.Values.Max(n => n.Status);

            Status = new List<SensorStatus> { statusFromNodes, statusFromSensors }.Max();
        }
    }
}
