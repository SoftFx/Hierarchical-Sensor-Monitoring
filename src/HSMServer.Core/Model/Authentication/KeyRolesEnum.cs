using HSMCommon.Attributes;
using System;

namespace HSMServer.Core.Model.Authentication
{
    [SwaggerIgnore]
    [Obsolete]
    public enum ProductRoleEnum    {
        ProductManager = 0,
        ProductViewer = 1
    }
    [SwaggerIgnore]
    public enum KeyRolesEnum
    {
        Viewer,
        Feeder,
        Admin = 255
    }
}
