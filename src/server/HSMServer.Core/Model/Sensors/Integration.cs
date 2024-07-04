using System;


namespace HSMServer.Core.Model
{
    [Flags]
    public enum Integration : int
    {
        None = 0,
        Grafana = 1,
    }
}
