using System.Reflection;

namespace HSMPingModule.Extensions;

internal static class VersionExtension
{
    internal static string RemoveTailZeroes(this Version version) => version.Revision == 0 ? version.ToString(3) : version.ToString();

    internal static Version GetVersion(this AssemblyName assembly) => assembly.Version ?? new Version("0.0.0");
}