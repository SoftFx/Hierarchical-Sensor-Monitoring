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

            UpdateNodeStatuses();
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

        public TreeViewModel Update(List<SensorData> sensors)
        {
            foreach (var sensor in sensors)
            {
                AddSensor(sensor);   
            }
            UpdateNodeStatuses();
            return this;
        }
        public TreeViewModel Update(TreeViewModel newModel)
        {
            foreach (var path in newModel.Paths)
            {
                if (!Paths.Contains(path))
                    Paths.Add(path);
            }

            foreach(var node in newModel.Nodes)
            {
                var existingNode = Nodes?.FirstOrDefault(x => x.Name.Equals(node.Name));

                if (Nodes == null)
                    Nodes = new List<NodeViewModel> { node };

                else if (existingNode == null)
                    Nodes.Add(node);

                else existingNode = existingNode.Update(node); 
            }

            UpdateNodeStatuses();
            return this;
        }

        public void UpdateNodeStatuses()
        {
            foreach (var node in Nodes)
            {
                node.UpdateStatus();
            }
        }
    }
}
