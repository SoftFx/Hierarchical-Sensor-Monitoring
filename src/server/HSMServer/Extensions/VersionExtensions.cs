using System;
using System.Reflection;

namespace HSMServer.Extensions;

public static class VersionExtensions
{
    public static string RemoveTailZeroes(this Version version) => 
        version.Revision == 0 ? version.ToString(3) : version.ToString();

    public static Version GetVersion(this AssemblyName assembly) => assembly.Version ?? new Version("0.0.0");
}