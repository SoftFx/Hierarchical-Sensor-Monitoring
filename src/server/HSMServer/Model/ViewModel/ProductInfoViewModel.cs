﻿using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Model.TreeViewModels;

namespace HSMServer.Model.ViewModel
{
    public class ProductInfoViewModel : NodeInfoBaseViewModel
    {
        public string Name { get; }

        public NodeViewModel Parent { get; }


        public ProductInfoViewModel() { }

        internal ProductInfoViewModel(ProductNodeViewModel product) : base(product)
        {
            Name = product.Name;
            Parent = product.Parent;

            ExpectedUpdateInterval = new(product.ExpectedUpdateInterval.ToModel(), _predefinedIntervals);
        }


        internal ProductInfoViewModel Update(ProductUpdate updatedModel)
        {
            ExpectedUpdateInterval = new(updatedModel.ExpectedUpdateInterval, _predefinedIntervals);

            return this;
        }
    }
}
