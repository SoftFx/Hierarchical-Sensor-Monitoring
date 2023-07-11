using System;

namespace HSMServer.Extensions;

public static class VersionExtensions
{
    public static string RemoveTailZeroes(this Version version) => 
        version.Revision == 0 ? version.ToString(3) : version.ToString();
}