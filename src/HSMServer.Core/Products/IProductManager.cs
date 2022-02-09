using System.Collections.Generic;
using HSMServer.Core.Model;

namespace HSMServer.Core.Products
{
    public interface IProductManager
    {
        List<Product> Products { get; }
        void RemoveProduct(string name);
        void AddProduct(string name);
        void UpdateProduct(Product product);
        string GetProductNameByKey(string key);
        Product GetProductByName(string name);
        Product GetProductByKey(string key);
    }
}