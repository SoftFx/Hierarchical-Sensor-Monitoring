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
            foreach (var sensor in sensors)
            {
                var path = (sensor.Product + "/" + sensor.Path);
                path = path.Substring(0, path.LastIndexOf('/'));
                path = path.Replace('/', '_');

                if (Paths == null)
                    Paths = new List<string> { path };
                else if (Paths.FirstOrDefault(x => x.Equals(path)) == null)
                    Paths.Add(path);

                var existingNode = Nodes?.FirstOrDefault(x => x.Name.Equals(sensor.Product));

                if (Nodes == null)
                    Nodes = new List<NodeViewModel> { new NodeViewModel(sensor.Product, sensor.Product, sensor) };

                else if (existingNode == null)
                    Nodes.Add(new NodeViewModel(sensor.Product, sensor.Product, sensor));

                else
                    existingNode.AddSensor(sensor.Product, sensor);                          
            }
        }
    }
}
