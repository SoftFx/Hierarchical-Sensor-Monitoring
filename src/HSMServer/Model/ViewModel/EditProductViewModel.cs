using HSMServer.DataLayer.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public class EditProductViewModel
    {
        public string ProductName { get; set; }
        public string ProductKey { get; set; }
        public List<KeyValuePair<Guid, string>> UsersRights { get; set; }

        public List<ExtraKeyViewModel> ExtraKeys { get; set; }

        public EditProductViewModel(Product product)
        {
            ProductName = product.Name;
            ProductKey = product.Key;
            UsersRights = product.UsersRights?.Select(r => new KeyValuePair<Guid, string>
            (r.Key, r.Value.ToString())).ToList();
            ExtraKeys = product.ExtraKeys?.Select(k => new ExtraKeyViewModel(product.Key, k)).ToList();
        }
    }
}
