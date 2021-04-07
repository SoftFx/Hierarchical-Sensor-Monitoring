using System.Collections.Generic;
using HSMClientWPFControls.Objects;

namespace HSMClientWPFControls.ConnectorInterface
{
    public interface IProductsConnector
    {
        public List<ProductInfo> GetProductsList();
        public ProductInfo AddNewProduct(string name);
        public bool RemoveProduct(string name);
    }
}