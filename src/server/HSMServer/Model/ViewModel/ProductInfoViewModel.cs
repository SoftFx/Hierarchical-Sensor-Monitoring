using System.Collections.Generic;
using System.Linq;
using HSMServer.Model.TreeViewModel;
using SensorStatus = HSMServer.Model.TreeViewModel.SensorStatus;

namespace HSMServer.Model.ViewModel
{
    public class ProductInfoViewModel : NodeInfoBaseViewModel
    {
        public List<(SensorStatus Key, int Value)> NodeStatuses { get; set; } = new();

        public List<KeyValuePair<SensorStatus, int>> SensorStatuses { get; set; } = new();
        
        
        public NodeViewModel Parent { get; }
        
        public string Name { get; }
        
        
        public int TotalNodes { get; set; }
        
        public int TotalSensors { get; set; }

        public string TotalSensorTypesMessage { get; set; }
        

        internal ProductInfoViewModel(ProductNodeViewModel product) : base(product)
        {
            Name = product.Name;
            Parent = product.Parent;
            Status = product.Status;
            LastUpdateTime = product.UpdateTime;
            
            TotalNodes = product.Nodes.Count;
            TotalSensors = product.AllSensorsCount;

            NodeStatuses = product.Nodes.Values.GroupBy(x => x.Status).Select(s => (s.Key, s.Count())).ToList();
            SensorStatuses = product.TotalSensorsByStatuses.OrderBy(x => x.Key).ToList();
            TotalSensorTypesMessage =  string.Join("\n", product.TotalSensorsByType.Select(x => $"{x.Value} {x.Key}").ToArray());
        }
    }
}
