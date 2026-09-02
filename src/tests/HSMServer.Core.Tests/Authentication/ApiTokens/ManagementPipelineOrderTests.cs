using System.IO;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // Pins the pipeline ORDER in ApplicationServiceExtensions.ConfigureMiddleware — the
    // one property of the isolation design the per-middleware unit tests cannot see: the
    // guards must run after routing (endpoint metadata is available) and BEFORE
    // authentication/authorization and UserProcessorMiddleware (which would otherwise
    // touch a principal the guards meant to reject). Reordering would pass every other
    // test in this suite, so this test reads the source and fails on a swap.
    public class ManagementPipelineOrderTests
    {
        private const string RelativeSource = "src/server/HSMServer/Extensions/ApplicationServiceExtensions.cs";


        private static string ReadPipelineSource()
        {
            var directory = new DirectoryInfo(System.AppContext.BaseDirectory);

            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
                directory = directory.Parent;

            var source = directory is null ? null : Path.Combine(directory.FullName, RelativeSource.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(source is not null && File.Exists(source),
                $"Cannot locate {RelativeSource} (resolved: {source ?? "<none>"}, started from {System.AppContext.BaseDirectory}) — the pipeline-order pin needs the repo checkout");

            return File.ReadAllText(source);
        }


        [Fact]
        public void Guards_RunAfterRouting_AndBeforeAuthenticationAndUserProcessor()
        {
            var body = ExtractConfigureMiddlewareBody(ReadPipelineSource());

            string[] pinnedOrder =
            [
                "UseRouting",
                "ManagementApiGuardMiddleware",
                "LegacyBearerGuardMiddleware",
                "UseAuthentication",
                "UseAuthorization",
                "UserProcessorMiddleware",
            ];

            var lastIndex = -1;

            foreach (var marker in pinnedOrder)
            {
                var index = body.IndexOf(marker, System.StringComparison.Ordinal);

                Assert.True(index >= 0, $"{marker} is not registered in ConfigureMiddleware");
                Assert.True(index > lastIndex, $"{marker} must come after the previous middleware in the pinned order");

                lastIndex = index;
            }
        }

        // The middleware block lives in ConfigureMiddleware; ordering is only meaningful
        // within that method, not across the whole file.
        private static string ExtractConfigureMiddlewareBody(string source)
        {
            var start = source.IndexOf("ConfigureMiddleware", System.StringComparison.Ordinal);

            Assert.True(start >= 0, "ConfigureMiddleware not found");

            var end = source.IndexOf("InitStorages", start, System.StringComparison.Ordinal);

            return source[start..end];
        }
    }
}
