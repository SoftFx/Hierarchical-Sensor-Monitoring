using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using System.Collections.Generic;

namespace HSMServer.Core.MonitoringCoreInterface
{
    public interface IProductsInterface
    {
        public Product GetProduct(string productKey);
        List<Product> GetProducts(User user);
        List<Product> GetAllProducts();
        bool AddProduct(User user, string productName, out Product product, out string error);
        bool RemoveProduct(Product product, out string error);
        bool RemoveProduct(string productKey, out string error);
        bool RemoveProduct(User user, string productName, out Product product, out string error);
        void UpdateProduct(User user, Product product);
    }
}