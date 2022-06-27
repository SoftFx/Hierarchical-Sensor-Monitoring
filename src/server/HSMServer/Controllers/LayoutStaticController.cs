using System;
using System.Reflection;

namespace HSMServer.Controllers.LayoutStaticController
{
    public static class LayoutStaticController
    {
        public static string Version { get; } = ReadCurrentVersion();


        private static string ReadCurrentVersion()
        {
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;

                if (version is not null)
                    return $"Current version: {version.Major}.{version.Minor}.{version.Build}";
            }
            catch (Exception) { }

            return string.Empty;
        }
    }
}
