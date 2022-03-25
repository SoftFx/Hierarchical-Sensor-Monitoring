using HSMCommon.Attributes;

namespace HSMServer.Core.Model.Authentication
{
    [SwaggerIgnore]
    public enum KeyRolesEnum
    {
        Viewer,
        Feeder,
        Admin = 255
    }
}
