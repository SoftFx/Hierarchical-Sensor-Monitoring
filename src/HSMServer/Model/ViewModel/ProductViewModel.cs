using HSMServer.DataLayer.Model;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    public class ProductViewModel
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public DateTime CreationDate { get; set; }
        //public Guid ManagerId { get; set; }
        public List<ExtraProductKey> ExtraProductKeys { get; set; }

        public List<string> Viewers { get;set; }

        public ProductViewModel(Product product, List<string> viewers)
        {
            Key = product.Key;
            Name = product.Name;
            CreationDate = product.DateAdded;
            Viewers = viewers;
            //ManagerId = product.ManagerId;
            ExtraProductKeys = product.ExtraKeys;
        }
    }
}