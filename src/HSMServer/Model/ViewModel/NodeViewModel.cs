using HSMSensorDataObjects;
using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public class NodeViewModel
    {
        public IComparer<NodeViewModel> NodeComparer { get; set; }
        public IComparer<SensorViewModel> SensorComparer { get; set; }
        public int Count { get; set; }
        public string Name { get; set; }

        public string Path { get; set; }

        public SensorStatus Status { get; set; }

        public DateTime UpdateTime { get; set; }

        public NodeViewModel Parent { get; set; }

        public SortedSet<NodeViewModel> Nodes { get; set; }

        public SortedSet<SensorViewModel> Sensors { get; set; }

        public NodeViewModel(string name, string path, SensorData sensor, NodeViewModel parent,
            IComparer<NodeViewModel> nodeComparer, IComparer<SensorViewModel> sensorComparer)
        {
            Name = name;
            Path = path;
            Status = sensor.Status;
            Parent = parent;
            NodeComparer = nodeComparer;
            SensorComparer = sensorComparer;

            AddSensor(path, sensor);
            ModifyUpdateTime();
        }

        public NodeViewModel(NodeViewModel model, NodeViewModel parent,
            IComparer<NodeViewModel> nodeComparer, IComparer<SensorViewModel> sensorComparer)
        {
            Name = model.Name;
            Path = model.Path;
            Status = model.Status;
            Parent = parent;
            UpdateTime = model.UpdateTime;
            Count = model.Count;
            NodeComparer = nodeComparer;
            SensorComparer = sensorComparer;
        }

        public NodeViewModel() { }

        public NodeViewModel Clone(NodeViewModel parent)
        {
            var node = new NodeViewModel();
            node.Name = Name;
            node.Path = Path;
            node.Status = Status;
            node.Parent = parent;
            node.UpdateTime = UpdateTime;
            node.Count = Count;

            node.NodeComparer = NodeComparer is NameNodeComparer 
                ? new NameNodeComparer()
                : new LastTimeUpdateNodeComparer();

            node.SensorComparer = SensorComparer is NameSensorComparer
                ? new NameSensorComparer()
                : new LastTimeUpdateSensorComparer();

            if (Nodes != null && Nodes.Count > 0)
            {
                node.Nodes = new SortedSet<NodeViewModel>(node.NodeComparer);

                foreach(var child in Nodes)
                {
                    node.Nodes.Add(child.Clone(node));
                }
            }

            if (Sensors != null && Sensors.Count > 0)
            {

                node.Sensors = new SortedSet<SensorViewModel>(node.SensorComparer);

                foreach(var sensor in Sensors)
                {
                    node.Sensors.Add(sensor.Clone());
                }
            }

            return node;
        }

        public void AddSensor(string path, SensorData sensor)
        {
            var nodes = sensor.Path.Split('/');

            if (nodes.Length == 1)
            {
                if (Sensors == null)
                {
                    Sensors = new SortedSet<SensorViewModel>(SensorComparer);
                    Sensors.Add(new SensorViewModel(nodes[0], sensor));
                }


                var existingSensor = Sensors.FirstOrDefault(s => s.Name == nodes[0]);
                if (existingSensor == null)
                {
                    Sensors.Add(new SensorViewModel(nodes[0], sensor));
                }
                else
                {
                    existingSensor.Update(sensor);
                }

            }
            else
            {
                sensor.Path = sensor.Path.Substring(nodes[0].Length + 1, sensor.Path.Length - nodes[0].Length - 1);
                var existingNode = Nodes?.FirstOrDefault(x => x.Name.Equals(nodes[0]));
                path += $"/{nodes[0]}";

                if (Nodes == null)
                {
                    Nodes = new SortedSet<NodeViewModel>(NodeComparer);
                    Nodes.Add(new NodeViewModel(nodes[0], path, sensor, this, 
                        NodeComparer, SensorComparer));
                }

                else if (existingNode == null)
                {
                    Nodes.Add(new NodeViewModel(nodes[0], path, sensor, this, 
                        NodeComparer, SensorComparer));
                }
                else
                    existingNode.AddSensor(path, sensor);
            }
        }

        public NodeViewModel GetNode(string path)
        {
            if (Nodes != null)
                foreach (var node in Nodes)
                {
                    if (node.Path.Equals(path)) return node;

                    var existingNode = node.GetNode(path);
                    if (existingNode != null) return existingNode;
                }

            return null;
        }
      
        public void ChangeComparer(NodeViewModel oldNode, IComparer<NodeViewModel> nodeComparer,
            IComparer<SensorViewModel> sensorComparer)
        {
            if (oldNode.Nodes != null && oldNode.Nodes.Count > 0)
            {
                Nodes = new SortedSet<NodeViewModel>(nodeComparer);

                foreach (var node in oldNode.Nodes)
                {
                    var newNode = new NodeViewModel(node, this, nodeComparer, sensorComparer);
                    Nodes.Add(newNode);
                    newNode.ChangeComparer(node, nodeComparer, sensorComparer);
                }
            }

            if (oldNode.Sensors != null && oldNode.Sensors.Count > 0)
            {
                Sensors = new SortedSet<SensorViewModel>(sensorComparer);

                foreach (var sensor in oldNode.Sensors)
                    Sensors.Add(new SensorViewModel(sensor));
            }
        }

        public void Recursion()
        {
            int count = 0;
            if (Nodes != null && Nodes.Count > 0)
            {
                foreach (var node in Nodes)
                {
                    node.Recursion();
                    count += node.Count;
                }
            }

            Count = count + (Sensors?.Count ?? 0);
            //if (Sensors != null && Sensors.Count > 0)
            ModifyUpdateTime();
            ModifyStatus();
        }

        public void ModifyUpdateTime()
        {
            var sensorMaxTime = Sensors?.Max(x => x.Time);
            var nodeMaxTime = Nodes?.Max(x => x.UpdateTime);

            if (sensorMaxTime.HasValue && nodeMaxTime.HasValue)
                UpdateTime = sensorMaxTime.Value > nodeMaxTime.Value
                    ? sensorMaxTime.Value : nodeMaxTime.Value;
            else if (sensorMaxTime.HasValue)
                UpdateTime = sensorMaxTime.Value;
            else
                UpdateTime = nodeMaxTime.Value;
        }

        public void ModifyStatus()
        {
            var statusFromSensors = Sensors?.Max(s => s.Status) ?? SensorStatus.Unknown;
            var statusFromNodes = Nodes?.Max(n => n.Status) ?? SensorStatus.Unknown;

            Status = new List<SensorStatus> { statusFromNodes, statusFromSensors }.Max();

        }
    }
}
