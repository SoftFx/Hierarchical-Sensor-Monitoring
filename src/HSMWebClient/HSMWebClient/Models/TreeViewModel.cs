using HSMCommon.Model.SensorsData;
using System.Collections.Generic;
using System.Linq;

namespace HSMWebClient.Models
{
    public class TreeViewModel
    {
        public List<NodeViewModel> Nodes { get; set; }

        public TreeViewModel(List<SensorData> sensors)
        {
            foreach (var sensor in sensors)
            {
                if (Nodes == null)
                    Nodes = new List<NodeViewModel> { new NodeViewModel(sensor.Product, sensor) };

                else if (Nodes.FirstOrDefault(x => x.Name.Equals(sensor.Product)) == null)
                    Nodes.Add(new NodeViewModel(sensor.Product, sensor));
            }
        }
    }
}
