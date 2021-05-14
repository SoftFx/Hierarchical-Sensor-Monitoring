using HSMCommon.Model.SensorsData;
using HSMSensorDataObjects;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public class NodeViewModel
    {
        public string Name { get; set; }

        public string Path { get; set; }

        public SensorStatus Status { get; set; }

        //public DateTime LastUpdate {get;set;}

        public List<NodeViewModel> Nodes { get; set; }

        public List<SensorViewModel> Sensors { get; set; }

        public NodeViewModel(string name, string path, SensorData sensor)
        {
            Name = name;
            Path = path.Replace('/', '_');
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

                else
                    Sensors.Add(new SensorViewModel(nodes[0], sensor));
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
    }
}
