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

    [SwaggerIgnore]
    [Flags]
    public enum KeyPermissions : long
    {
        CanSendSensorData = 1,
        CanAddProducts = 2,
        CanAddSensors = 4
    }

    [SwaggerIgnore]
    public enum KeyState : byte
    {
        Active = 0,
        Expired = 1,
        Blocked = 7
    }
}
