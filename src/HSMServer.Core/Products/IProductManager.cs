using System;
using System.Collections.Generic;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;

namespace HSMServer.Core.Products
{
    public interface IProductManager
    {
        //ToDo: change to GetProducts(User)
        List<Product> Products { get; }
        event Action<Product> RemovedProduct;
        Product AddProduct(string name);
        void AddProduct(ProductModel product);
        void RemoveProduct(string name);
        void RemoveProduct(ProductModel model);
        string GetProductNameByKey(string key);
        Product GetProductByName(string name);
        Product GetProductByKey(string key);
        Product GetProductCopyByKey(string key);
        List<Product> GetProducts(User user);
    }
}