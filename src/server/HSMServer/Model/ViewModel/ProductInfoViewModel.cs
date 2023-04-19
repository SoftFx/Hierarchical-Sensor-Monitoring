using System.Collections.Generic;
using System.Linq;
using HSMServer.Core.Model;
using HSMServer.Model.TreeViewModel;
using SensorStatus = HSMServer.Model.TreeViewModel.SensorStatus;

namespace HSMServer.Model.ViewModel
{
    public sealed class ProductInfoViewModel : NodeInfoBaseViewModel
    {
        public List<(SensorStatus Status, int Count)> NodeStatuses { get; set; } = new();

        public List<(SensorStatus Status, int Count)> SensorsStatuses { get; set; } = new();
        
        public List<(SensorType Type, int Count)> SensorsTypes { get; set; } = new();
        
        
        public NodeViewModel Parent { get; }
        
        public string Name { get; }
        
        
        public int TotalNodes { get; set; }
        
        public int TotalSensors { get; set; }

        public string TotalSensorTypesMessage { get; set; }
        
        public ProductInfoViewModel(){ }
        
        internal ProductInfoViewModel(ProductNodeViewModel product) : base(product)
        {
            Name = product.Name;
            Parent = product.Parent;
            Status = product.Status;
            LastUpdateTime = product.UpdateTime;
            
            TotalNodes = product.Nodes.Count;
            TotalSensors = product.Sensors.Count;

            NodeStatuses = product.Nodes.Values.GroupBy(x => x.Status).OrderBy(x => x.Key).Select(s => (s.Key, s.Count())).ToList();
            
            SensorsStatuses = product.Sensors.GroupBy(x => x.Value.Status).OrderBy(x => x.Key).Select(x => (x.Key, x.Count())).ToList();
            SensorsTypes = product.Sensors.GroupBy(x => x.Value.Type).OrderBy(x => x.Key).Select(x => (x.Key, x.Count())).ToList();
            TotalSensorTypesMessage = string.Join("\n", SensorsTypes.Select(x => $"{x.Type} {x.Count}").ToArray());
        }
    }
}
