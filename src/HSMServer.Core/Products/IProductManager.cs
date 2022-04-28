using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using System.Collections.Generic;

namespace HSMServer.Core.Products
{
    public interface IProductManager
    {
        //ToDo: change to GetProducts(User)
        List<Product> Products { get; }
        Product AddProduct(string name);
        void RemoveProduct(string name);
        string GetProductNameByKey(string key);
        Product GetProductByName(string name);
        Product GetProductByKey(string key);
        Product GetProductCopyByKey(string key);
        List<Product> GetProducts(User user);
    }
}