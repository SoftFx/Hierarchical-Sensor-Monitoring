using System;
using System.Reflection;

namespace HSMCommon.Extensions;

public static class VersionExtensions
{
    public static Version GetVersion(this AssemblyName assembly) => assembly.Version ?? new Version("0.0.0");
}