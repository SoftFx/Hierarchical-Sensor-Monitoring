using System;
using System.Reflection;

namespace HSMServer.Model
{
    public static class ServerSettings
    {
        public static string Version
        {
            get
            {
                try
                {
                    var version = Assembly.GetExecutingAssembly().GetName().Version;

                    if (version is not null)
                        return $"{version.Major}.{version.Minor}.{version.Build}";
                }
                catch (Exception) { }

                return string.Empty;
            }
        }

        public static string Name => Assembly.GetExecutingAssembly().GetName().Name;
    }
}
