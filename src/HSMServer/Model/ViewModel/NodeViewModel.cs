using System;
using HSMSensorDataObjects;
using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Routing;

namespace HSMServer.Model.ViewModel
{
    public class NodeViewModel
    {
        public int Count { get; set; }
        public string Name { get; set; }

        public string Path { get; set; }

        public SensorStatus Status { get; set; }

        //public DateTime LastUpdate {get;set;}
        public DateTime UpdateTime { get; set; }

        public NodeViewModel Parent { get; set; }

        public List<NodeViewModel> Nodes { get; set; }

        public List<SensorViewModel> Sensors { get; set; }

        public NodeViewModel(string name, string path, SensorData sensor, NodeViewModel parent)
        {
            Name = name;
            Path = path;
            Status = sensor.Status;
            Parent = parent;

            AddSensor(path, sensor);
            ModifyUpdateTime();
        }

        public void AddSensor(string path, SensorData sensor)
        {
            var nodes = sensor.Path.Split('/');

            if (nodes.Length == 1)
            {
                if (Sensors == null)
                    Sensors = new List<SensorViewModel> { new SensorViewModel(nodes[0], sensor) };

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
                    Nodes = new List<NodeViewModel> { new NodeViewModel(nodes[0], path, sensor, this) };
                else if (existingNode == null)
                    Nodes.Add(new NodeViewModel(nodes[0], path, sensor, this));
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

        public NodeViewModel Update(NodeViewModel newModel)
        {
            Status = newModel.Status;
            if (newModel.Nodes != null)
                foreach (var node in newModel.Nodes)
                {
                    var existingNode = Nodes?.FirstOrDefault(x => x.Name.Equals(node.Name));
                    if (Nodes == null)
                        Nodes = new List<NodeViewModel> { node };

                    else if (existingNode == null)
                        Nodes.Add(node);

                    else
                        existingNode = existingNode.Update(node);
                }

            if (newModel.Sensors != null)
                foreach (var sensor in newModel.Sensors)
                {
                    if (Sensors == null)
                    {
                        Sensors = new List<SensorViewModel>() { sensor };
                        continue;
                    }

                    var existingSensor = Sensors?.FirstOrDefault(x => x.Name.Equals(sensor.Name));
                    if (existingSensor == null)
                    {
                        Sensors.Add(sensor);
                    }
                    else
                    {
                        existingSensor.Update(sensor);
                    }
                }

            return this;
        }

        public void SortByName()
        {
            if (Nodes != null && Nodes.Count > 0)
            {
                Nodes = Nodes.OrderBy(x => x.Name).ToList();

                foreach (var node in Nodes)
                    node.SortByName();
            }

            if (Sensors != null && Sensors.Count > 0)
                Sensors = Sensors.OrderBy(x => x.Name).ToList();
        }

        public void SortByTime()
        {
            if (Nodes != null && Nodes.Count > 0)
            {
                Nodes = Nodes.OrderByDescending(x => x.UpdateTime).ToList();

                foreach (var node in Nodes)
                    node.SortByTime();
            }

            if (Sensors != null && Sensors.Count > 0)
                Sensors = Sensors.OrderByDescending(x => x.Time).ToList();
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
            if (Sensors != null && Sensors.Count > 0)
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
