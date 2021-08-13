using HSMCommon.Model.SensorsData;
using HSMSensorDataObjects;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public class NodeViewModel
    {
        public int Count { get; set; }
        public string Name { get; set; }

        public string Path { get; set; }

        public SensorStatus Status { get; set; }

        //public DateTime LastUpdate {get;set;}

        public List<NodeViewModel> Nodes { get; set; }

        public List<SensorViewModel> Sensors { get; set; }

        public NodeViewModel(string name, string path, SensorData sensor)
        {
            Name = name;
            Path = path;//.Replace('/', '_');
            Status = sensor.Status;

            AddSensor(path, sensor);
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
                    Nodes = new List<NodeViewModel> { new NodeViewModel(nodes[0], path, sensor) };
                else if (existingNode == null)
                    Nodes.Add(new NodeViewModel(nodes[0], path, sensor));
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

        public void UpdateStatus()
        {
            SensorStatus statusFromSensors = SensorStatus.Unknown;
            SensorStatus statusFromNodes = SensorStatus.Unknown;
            if (Nodes != null && Nodes.Any())
            {
                foreach (var node in Nodes)
                {
                    node.UpdateStatus();
                }

                statusFromNodes = Nodes.Max(n => n.Status);
            }

            if (Sensors != null && Sensors.Any())
            {
                statusFromSensors = Sensors.Max(s => s.Status);
            }

            Status = new List<SensorStatus> {statusFromNodes, statusFromSensors}.Max();
        }

        public void UpdateSensorsCount()
        {
            int count = 0;
            if (Nodes != null && Nodes.Any())
            {
                foreach (var node in Nodes)
                {
                    node.UpdateSensorsCount();
                    count += node.Count;
                }
            }

            Count = count + (Sensors?.Count ?? 0);
        }
    }
}
