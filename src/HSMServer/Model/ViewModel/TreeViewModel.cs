using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensor;
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
            var path = (sensor.Product + "/" + sensor.Path); //product/path/...
            //path = path.Substring(0, path.LastIndexOf('/')); //without sensor

            if (Paths.FirstOrDefault(x => x.Equals(path)) == null)
                Paths.Add(path);

            var existingNode = Nodes?.FirstOrDefault(x => x.Name.Equals(sensor.Product));
            if (existingNode == null)
            {
                Nodes?.Add(new NodeViewModel(sensor.Product, sensor.Product, sensor, null));
            }
            else
            {
                existingNode.AddSensor(sensor.Product, sensor);
            }
        }

        private void RemoveSensor(SensorData sensor)
        {
            var path = (sensor.Product + "/" + sensor.Path);
            path = path.Substring(0, path.LastIndexOf('/'));
            var sensorName = sensor.Path.Substring(sensor.Path.LastIndexOf('/') + 1);

            var node = GetNode(path);
            if (node == null) return;

            var existingSensor = node.Sensors?.FirstOrDefault(s => s.Name.Equals(sensorName));
            node.Sensors?.Remove(existingSensor);

            while(node != null 
                && (node.Sensors == null || node.Sensors.Count == 0)
                && (node.Nodes == null || node.Nodes.Count == 0))
            {
                var parent = node.Parent;

                if (node.Parent != null)
                {
                    node.Parent.Nodes.Remove(node);
                    Paths.Remove(path);
                }
                else RemoveProduct(node.Path);

                node = parent;
            }
        }

        private void RemoveProduct(string product)
        {
            var node = GetNode(product);
            if (node == null) return;

            if (node.Sensors != null && node.Sensors.Count > 0)
                node.Sensors.Clear();

            if (node.Nodes != null && node.Nodes.Count > 0)
                node.Nodes.Clear();

            Nodes.Remove(node);
            Paths.Remove(product);
        }

        public NodeViewModel GetNode(string path)//with product
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
                if (sensor.TransactionType == TransactionType.Add
                    || sensor.TransactionType == TransactionType.Unknown
                    || sensor.TransactionType == TransactionType.Update)
                    AddSensor(sensor);

                if (sensor.TransactionType == TransactionType.Delete
                    && string.IsNullOrEmpty(sensor.Path))
                    RemoveProduct(sensor.Product);

                else if (sensor.TransactionType == TransactionType.Delete)
                    RemoveSensor(sensor);
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
