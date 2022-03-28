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
        protected readonly IDatabaseCore _databaseCore;
        protected readonly IProductManager _productManager;
        protected WorkerServiceBase(IDatabaseCore databaseCore, IProductManager productManager)
        {
            _databaseCore = databaseCore;
            _productManager = productManager;
        }
    }
}
