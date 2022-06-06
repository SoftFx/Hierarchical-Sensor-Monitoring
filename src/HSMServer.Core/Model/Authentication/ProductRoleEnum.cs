using HSMCommon.Attributes;
using System;

namespace HSMServer.Core.Model.Authentication
{
    [SwaggerIgnore]
    public enum ProductRoleEnum
    {
        ProductManager = 0,
        ProductViewer = 1
    }
}
