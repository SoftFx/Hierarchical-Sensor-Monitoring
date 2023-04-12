using System.Reflection;

namespace HSMCommon.Extensions;

public static class VersionExtensions
{
    public static string GetVersion(this AssemblyName assembly)
    {
        var version = assembly.Version;
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }
}