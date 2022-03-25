using HSMServer.Core.DataLayer;
using HSMServer.Core.Products;
using Microsoft.Extensions.Hosting;

namespace HSMServer.BackgroundTask
{
    /// <summary>
    /// One of possible base classes for HSM, with predefined DI classes, that might be needed
    /// </summary>
    public abstract class WorkerServiceBase : BackgroundService
    {
        protected readonly IDatabaseCore _databaseAdapter;
        protected readonly IProductManager _productManager;
        protected WorkerServiceBase(IDatabaseCore databaseAdapter, IProductManager productManager)
        {
            _databaseAdapter = databaseAdapter;
            _productManager = productManager;
        }
    }
}
