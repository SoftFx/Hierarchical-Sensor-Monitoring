using HSMDatabase.DatabaseWorkCore;

namespace HSMServer.Certificates
{
    public static class CertificatesConfig
    {
        public static DatabaseCore DatabaseCore { get; private set; }

        public static void InitializeConfig()
        {
            DatabaseCore = new DatabaseCore();
        }
    }
}
