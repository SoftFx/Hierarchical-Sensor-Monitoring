using System;
using System.Collections.Generic;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;

namespace HSMServer.Core.Products
{
    public interface IProductManager
    {
        event Action<string> RemovedProduct;
        List<Product> Products { get; }
        void AddProduct(string name);
        void RemoveProduct(string name);
        void UpdateProduct(Product product);
        string GetProductNameByKey(string key);
        Product GetProductByName(string name);
        Product GetProductByKey(string key);
        Product GetProductCopyByKey(string key);
        List<Product> GetProducts(User user);
    }
}