using System;


namespace HSMServer.Core.Model
{
    [Flags]
    public enum DefaultAlertsOptions : long
    {
        None = 0,
        DisableTtl = 1,
        DisableStatusChange = 2,
    }
}
