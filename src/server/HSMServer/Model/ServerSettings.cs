using System.Reflection;

namespace HSMServer.Model
{
    public static class ServerSettings
    {
        public static string Version { get; }

        public static string Name { get; }


        static ServerSettings()
        {
            var assembly = Assembly.GetExecutingAssembly().GetName();
            var version = assembly.Version;

            Name = assembly.Name;

            if (version is not null)
                Version = $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }
}
