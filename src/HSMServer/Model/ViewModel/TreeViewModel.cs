using HSMCommon.Constants;
using HSMServer.Core.Model.Sensor;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public class TreeViewModel
    {
        public List<string> Paths { get; set; }
        public ConcurrentDictionary<string, NodeViewModel> Nodes { get; set; }

        public TreeViewModel(List<SensorData> sensors)
        {
            Nodes = new ConcurrentDictionary<string, NodeViewModel>();
            Paths = new List<string>();

            foreach (var sensor in sensors)
            {
                AddSensor(sensor);
            }

            UpdateNodeCharacteristics();
        }

        public TreeViewModel() { }

        private void AddSensor(SensorData sensor)
        {
            var path = $"{sensor.Product}{CommonConstants.SensorPathSeparator}{sensor.Path}"; //product/path/...
            path = path.Substring(0, path.LastIndexOf(CommonConstants.SensorPathSeparator)); //without sensor

            if (Paths.FirstOrDefault(x => x.Equals(path)) == null)
                Paths.Add(path);

            Nodes.TryGetValue(sensor.Product, out var existingNode);
            if (existingNode == null)
            {
                Nodes[sensor.Product] = new NodeViewModel(sensor.Product, sensor.Product, sensor, null);
            }
            else
            {
                existingNode.AddSensor(sensor.Product, sensor);
            }
        }

        private void RemoveSensor(SensorData sensor)
        {
            var path = $"{sensor.Product}{CommonConstants.SensorPathSeparator}{sensor.Path}";
            path = path.Substring(0, path.LastIndexOf(CommonConstants.SensorPathSeparator));
            var sensorName = sensor.Path.Substring(sensor.Path.LastIndexOf(CommonConstants.SensorPathSeparator) + 1);

            var node = GetNode(path);
            if (node == null) 
                return;

            node.Sensors?.Remove(sensorName, out _);

            while (node != null
                && (node.Sensors == null || node.Sensors.IsEmpty)
                && (node.Nodes == null || node.Nodes.IsEmpty))
            {
                var parent = node.Parent;

                if (node.Parent != null)
                {
                    node.Parent.Nodes.Remove(node.Name, out _);
                    Paths.Remove(path);
                }
                else 
                    RemoveProduct(node.Path);

                node = parent;
            }
        }

        private void RemoveProduct(string product)
        {
            var node = GetNode(product);
            if (node == null) 
                return;

            if (node.Sensors != null && !node.Sensors.IsEmpty)
                node.Sensors.Clear();

            if (node.Nodes != null && !node.Nodes.IsEmpty)
                node.Nodes.Clear();

            Nodes.Remove(product, out _);
            Paths.Remove(product);
        }

        public NodeViewModel GetNode(string path)//with product
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
            foreach (var (_, node) in Nodes)
            {
                node.Recursion();
            }
        }

        public TreeViewModel Clone()
        {
            var tree = new TreeViewModel();

            if (Paths != null && Paths.Count > 0)
            {
                tree.Paths = new List<string>(Paths);
            }

            if (Nodes != null && !Nodes.IsEmpty)
            {
                tree.Nodes = new ConcurrentDictionary<string, NodeViewModel>();

                foreach(var (_, node) in Nodes)
                {
                    tree.Nodes[node.Name] = node.Clone(null);
                }
            }

            return tree;
        }
    }
}
