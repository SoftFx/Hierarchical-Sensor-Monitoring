using HSMCommon.Constants;
using HSMSensorDataObjects;
using HSMServer.Core.Model.Sensor;
using HSMServer.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public class NodeViewModel
    {
        private const int NodeNameMaxLength = 35;

        public int Count { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string EncodedPath => SensorPathHelper.Encode(Path);
        public SensorStatus Status { get; set; }
        public DateTime UpdateTime { get; set; }
        public NodeViewModel Parent { get; set; }
        public ConcurrentDictionary<string, NodeViewModel> Nodes { get; set; }
        public ConcurrentDictionary<string, SensorViewModel> Sensors { get; set; }

        public NodeViewModel(string name, string path, SensorData sensor, NodeViewModel parent)
        {
            Name = name;
            Path = path;
            Status = sensor.Status;
            Parent = parent;

            AddSensor(path, sensor);
            ModifyUpdateTime();
        }

        public NodeViewModel(NodeViewModel model, NodeViewModel parent)
        {
            Name = model.Name;
            Path = model.Path;
            Status = model.Status;
            Parent = parent;
            UpdateTime = model.UpdateTime;
            Count = model.Count;
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

            if (Nodes != null && !Nodes.IsEmpty)
            {
                node.Nodes = new ConcurrentDictionary<string, NodeViewModel>();

                foreach (var (name, child) in Nodes)
                {
                    node.Nodes[name] = child.Clone(node);
                }
            }

            if (Sensors != null && !Sensors.IsEmpty)
            {

                node.Sensors = new ConcurrentDictionary<string, SensorViewModel>();

                foreach (var (name, sensor) in Sensors)
                {
                    node.Sensors[name] = sensor.Clone();
                }
            }

            return node;
        }

        public void AddSensor(string path, SensorData sensor)
        {
            var nodes = sensor.Path.Split(CommonConstants.SensorPathSeparator);

            if (nodes.Length == 1)
            {
                if (Sensors == null)
                {
                    Sensors = new ConcurrentDictionary<string, SensorViewModel>();
                }

                if (!Sensors.TryGetValue(nodes[0], out var existingSensor))
                {
                    Sensors[nodes[0]] = new SensorViewModel(nodes[0], sensor);
                }
                else
                {
                    existingSensor.Update(sensor);
                }
            }
            else
            {
                sensor.Path = sensor.Path.Substring(nodes[0].Length + 1, sensor.Path.Length - nodes[0].Length - 1);

                if (Nodes == null)
                {
                    Nodes = new ConcurrentDictionary<string, NodeViewModel>();
                }

                Nodes.TryGetValue(nodes[0], out var existingNode);
                path += $"{CommonConstants.SensorPathSeparator}{nodes[0]}";

                if (existingNode == null)
                {
                    Nodes[nodes[0]] = new NodeViewModel(nodes[0], path, sensor, this);
                }
                else
                    existingNode.AddSensor(path, sensor);
            }
        }

        public NodeViewModel GetNode(string path)
        {
            if (Nodes != null)
                foreach (var (_, node) in Nodes)
                {
                    if (node.Path.Equals(path))
                        return node;

                    var existingNode = node.GetNode(path);
                    if (existingNode != null)
                        return existingNode;
                }

            return null;
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
            var sensorMaxTime = (Sensors?.Values?.Count ?? 0) == 0 ? null : Sensors?.Values.Max(x => x.Time);
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

        public string GetShortName(string name) =>
            name.Length > NodeNameMaxLength ? $"{name[..NodeNameMaxLength]}..." : name;
    }
}
