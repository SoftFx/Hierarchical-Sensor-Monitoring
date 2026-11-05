using System.IO;
using HSMCommon;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace HSMServer.Authentication
{
    // Data Protection key-ring wiring (#1633), kept in one place so Program.cs
    // cannot register the pieces inconsistently:
    //   AddHsmDataProtection - persist the key ring (auth cookie + antiforgery)
    //                          into a caller-owned directory and pin the
    //                          application name that scopes the ring.
    //
    // Invariants (tested in DataProtectionKeyRingTests): a payload protected by
    // one provider validates with a fresh provider over the same directory —
    // which is what keeps sessions alive across a container recreate.
    public static class HsmDataProtectionServiceCollectionExtensions
    {
        public const string KeysFolderName = "DataProtection-Keys";

        // The application name is pinned explicitly instead of relying on the
        // environment default (entry assembly / content root): the ring stays
        // scoped if the entry point or the working directory ever changes, and
        // it can never collide with another application sharing the directory.
        public const string ApplicationName = "HSMServer";


        public static IServiceCollection AddHsmDataProtection(this IServiceCollection services, string keysDirectory)
        {
            // Same creation discipline as the Config directory itself
            // (ServerConfig static ctor): created up front so the repository
            // never races a first-use read against a missing volume path.
            FileManager.SafeCreateDirectory(keysDirectory);

            return services.AddDataProtection()
                           .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
                           .SetApplicationName(ApplicationName)
                           .Services;
        }
    }
}
