using System.Collections.Generic;
using HSMDatabase.Entity;

namespace HSMDatabase.EnvironmentDatabase
{
    internal interface IEnvironmentDatabase
    {
        void AddProductToList(string productName);
        List<string> GetProductsList();
        ProductEntity GetProductInfo(string productName);
        void PutProductInfo(ProductEntity product);
        void RemoveProductInfo(string name);
        void RemoveProductFromList(string name);
    }
}