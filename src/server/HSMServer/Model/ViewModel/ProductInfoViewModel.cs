using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using System.Collections.Generic;
using System.Linq;
using SensorStatus = HSMServer.Model.TreeViewModel.SensorStatus;

namespace HSMServer.Model.ViewModel
{
    public sealed class ProductInfoViewModel : NodeInfoBaseViewModel
    {
        public List<(SensorStatus Status, int Count)> SensorsStatuses { get; } = new();

        public List<(SensorStatus Status, int Count)> NodeStatuses { get; } = new();

        public List<(SensorType Type, int Count)> SensorsTypes { get; } = new();


        public string TotalSensorTypesMessage { get; }

        public int TotalSensors { get; }

        public int TotalNodes { get; }


        public ProductInfoViewModel() { }

        internal ProductInfoViewModel(ProductNodeViewModel product) : base(product)
        {
            SensorsStatuses = product.Sensors.Values.ToGroupedList(x => x.Status.ToEmpty(x.HasData));
            NodeStatuses = product.Nodes.Values.ToGroupedList(x => x.Status.ToEmpty(x.HasData));
            SensorsTypes = product.Sensors.Values.ToGroupedList(x => x.Type);

            TotalNodes = product.Nodes.Count;
            TotalSensors = product.Sensors.Count;
            TotalSensorTypesMessage = string.Join("\n", SensorsTypes.Select(x => $"{x.Type} {x.Count}").ToArray());
        }

        protected override GeneralInfoUpdate GetInfoUpdate() => new(this);
    }
}
