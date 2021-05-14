using HSMCommon.Model.SensorsData;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public class TreeViewModel
    {

        public List<NodeViewModel> Nodes { get; set; }

        public TreeViewModel(List<SensorData> sensors)
        {
            foreach (var sensor in sensors)
            {
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
