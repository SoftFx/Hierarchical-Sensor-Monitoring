﻿using HSMCommon.Attributes;

namespace HSMServer.Core.Model.Authentication
{
    [SwaggerIgnore]
    public enum ProductRoleEnum
    {
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
