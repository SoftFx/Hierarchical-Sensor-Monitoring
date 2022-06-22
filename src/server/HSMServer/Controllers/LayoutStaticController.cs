using HSMCommon.Constants;
using System;
using System.IO;

namespace HSMServer.Controllers.LayoutStaticController
{
    public static class LayoutStaticController
    {
        public static string Version { get; } = ReadCurrentVersion();


        private static string ReadCurrentVersion()
        {
            try
            {
                string versionFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    ConfigurationConstants.VersionFileName);
                string content = File.ReadAllText(versionFilePath);

                if (!string.IsNullOrEmpty(content))
                    return $"Current version: {content}";
            }
            catch (Exception) { }

            return string.Empty;
        }
    }
}
