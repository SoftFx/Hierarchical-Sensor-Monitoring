using System;
using HSMServer.Core.Model;

namespace HSMServer.Core.Interfaces.Services;

public interface IPermissionService
{
    bool TryGetKey(Guid id, out AccessKeyModel key, out string message);

    bool TryGetProduct(Guid id, out ProductModel product, out string message);

    bool CheckWritePermissions(ProductModel product, AccessKeyModel accessKey, ReadOnlySpan<string> pathParts, out string message);
}