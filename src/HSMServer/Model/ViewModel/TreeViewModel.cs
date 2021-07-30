using HSMCommon.Model.SensorsData;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public class TreeViewModel
    {
        public List<string> Paths { get; set; }
        public List<NodeViewModel> Nodes { get; set; }
        public TreeViewModel(List<SensorData> sensors)
        {
            Nodes = new List<NodeViewModel>();
            Paths = new List<string>();
            foreach (var sensor in sensors)
            {
                AddSensor(sensor);
            }

            UpdateNodeCharacteristics();
        }

        private void AddSensor(SensorData sensor)
        {
            var path = (sensor.Product + "/" + sensor.Path);
            path = path.Substring(0, path.LastIndexOf('/'));
            path = path.Replace('/', '_');

            if (Paths.FirstOrDefault(x => x.Equals(path)) == null)
                Paths.Add(path);

            var existingNode = Nodes?.FirstOrDefault(x => x.Name.Equals(sensor.Product));
            if (existingNode == null)
            {
                Nodes?.Add(new NodeViewModel(sensor.Product, sensor.Product, sensor));
            }
            else
            {
                existingNode.AddSensor(sensor.Product, sensor);
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

        public TreeViewModel Update(List<SensorData> sensors)
        {
            foreach (var sensor in sensors)
            {
                AddSensor(sensor);   
            }
            UpdateNodeCharacteristics();
            return this;
        }

        public void UpdateNodeCharacteristics()
        {
            foreach (var node in Nodes)
            {
                node.UpdateStatus();
                node.UpdateSensorsCount();
            }
        }
    }
}
