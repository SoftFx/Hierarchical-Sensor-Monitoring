using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensor;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public class TreeViewModel
    {
        public IComparer<NodeViewModel> NodeComparer { get; set; }
        public IComparer<SensorViewModel> SensorComparer { get; set; }

        public List<string> Paths { get; set; }

        public SortedSet<NodeViewModel> Nodes { get; set; }
        public TreeViewModel(List<SensorData> sensors)
        {
            NodeComparer = new NameNodeComparer();
            SensorComparer = new NameSensorComparer(); 

            Nodes = new SortedSet<NodeViewModel>(NodeComparer);
            Paths = new List<string>();

            foreach (var sensor in sensors)
            {
                AddSensor(sensor);
            }

            UpdateNodeCharacteristics();

            var oldTree = Clone();
            ChangeComparer(oldTree);
        }

        public TreeViewModel() { }

        private void AddSensor(SensorData sensor)
        {
            var path = (sensor.Product + "/" + sensor.Path); //product/path/...
            path = path.Substring(0, path.LastIndexOf('/')); //without sensor

            if (Paths.FirstOrDefault(x => x.Equals(path)) == null)
                Paths.Add(path);

            var existingNode = Nodes?.FirstOrDefault(x => x.Name.Equals(sensor.Product));
            if (existingNode == null)
            {
                Nodes?.Add(new NodeViewModel(sensor.Product, sensor.Product, sensor, null, 
                    NodeComparer, SensorComparer));
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
            var oldTree = Clone();
            Nodes = new SortedSet<NodeViewModel>(NodeComparer);

            ChangeComparer(oldTree);

            return this;
        }

        public TreeViewModel SortByName()
        {
            if (NodeComparer is IComparer<NameNodeComparer>
                && SensorComparer is IComparer<NameSensorComparer>) return this;

            UpdateNodeCharacteristics();
            var oldTree = Clone();

            NodeComparer = new NameNodeComparer();
            SensorComparer = new NameSensorComparer();
            Nodes = new SortedSet<NodeViewModel>(NodeComparer);

            ChangeComparer(oldTree);

            return this;
        }

        public TreeViewModel SortByTime()
        {
            if (NodeComparer is IComparer<LastTimeUpdateNodeComparer>
                && SensorComparer is IComparer<LastTimeUpdateSensorComparer>) return this;

            UpdateNodeCharacteristics();
            var oldTree = Clone();

            NodeComparer = new LastTimeUpdateNodeComparer();
            SensorComparer = new LastTimeUpdateSensorComparer();
            Nodes = new SortedSet<NodeViewModel>(NodeComparer);

            ChangeComparer(oldTree);

            return this;
        }

        private void ChangeComparer(TreeViewModel oldTree)
        {
            if (oldTree.Nodes != null && oldTree.Nodes.Count > 0)
            {
                foreach (var node in oldTree.Nodes)
                {
                    var newNode = new NodeViewModel(node, null, NodeComparer, SensorComparer);
                    Nodes.Add(newNode);

                    newNode.ChangeComparer(node, NodeComparer, SensorComparer);
                }
            }
        }

        public void UpdateNodeCharacteristics()
        {
            foreach (var node in Nodes)
            {
                node.Recursion();
            }
        }

        public TreeViewModel Clone()
        {
            var tree = new TreeViewModel();
            tree.NodeComparer = NodeComparer is NameNodeComparer 
                ? new NameNodeComparer()
                : new LastTimeUpdateNodeComparer();
            tree.SensorComparer = SensorComparer is NameSensorComparer
                ? new NameSensorComparer()
                : new LastTimeUpdateSensorComparer();

            if (Paths != null && Paths.Count > 0)
            {
                tree.Paths = new List<string>(Paths);
            }

            if (Nodes != null && Nodes.Count > 0)
            {
                tree.Nodes = new SortedSet<NodeViewModel>(tree.NodeComparer);

                foreach(var node in Nodes)
                {
                    tree.Nodes.Add(node.Clone(null));
                }
            }

            return tree;
        }
    }
}
