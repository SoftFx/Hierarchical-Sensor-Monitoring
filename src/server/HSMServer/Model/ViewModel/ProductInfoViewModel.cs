﻿using System.Collections.Generic;
using System.Linq;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.ViewModel
{
    public class ProductInfoViewModel : NodeInfoBaseViewModel
    {
        public string Name { get; }

        
        
        public int TotalNodes { get; set; }
        
        public string TotalNodesMessage { get; set; }
        
        public string TotalSensorsMessage { get; set; }
        
        public string TotalSensorsStatusesMessage { get; set; }
        
        public int TotalSensors { get; set; }

        public NodeViewModel Parent { get; }


        public ProductInfoViewModel() : base() { }

        internal ProductInfoViewModel(ProductNodeViewModel product) : base(product)
        {
            Name = product.Name;
            Parent = product.Parent;
            Status = product.Status;
            UpdateTime = product.UpdateTime;
            TotalNodes = product.Nodes.Count;
            TotalSensors = product.AllSensorsCount;
            GenerateTotalNodesMessage(product.Nodes.Values);
            
            TotalSensorsMessage =  string.Join("\n", product.TotalSensorsByType.Select(x => $"{x.Value} {x.Key}").ToArray());

            TotalSensorsStatusesMessage = string.Join(", ",
                product.TotalSensorsByStatuses.Select(x => $"{x.Value} - <i class='{x.Key.ToIcon()} ps-1 align-self-center'></i>").ToArray());
        }

        private void GenerateTotalNodesMessage(ICollection<ProductNodeViewModel> productNodeViewModels)
        {
            var statuses = new Dictionary<SensorStatus, int>();

            foreach (var node in productNodeViewModels)
            {
                if (statuses.TryGetValue(node.Status, out var _))
                {
                    statuses[node.Status]++;
                }
                else statuses.TryAdd(node.Status, 1);
            }
            
            TotalNodesMessage = string.Join(", ", statuses.Select(x => $"{x.Value} - <i class='{x.Key.ToIcon()} ps-1 align-self-center'></i>").ToArray());
        }
    }
}
