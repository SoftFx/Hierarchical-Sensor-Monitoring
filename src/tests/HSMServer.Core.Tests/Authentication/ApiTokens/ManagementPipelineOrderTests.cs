using System.IO;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // Pins the pipeline ORDER in ApplicationServiceExtensions.ConfigureMiddleware — the
    // one property of the isolation design the per-middleware unit tests cannot see: the
    // guards must run after routing (endpoint metadata is available) and BEFORE
    // authentication/authorization and UserProcessorMiddleware (which would otherwise
    // touch a principal the guards meant to reject). Since #1353 it also pins the two
    // error-contract registrations the unit tests cannot see: the /api exception
    // handler's placement (between the global /Error handler and the logging
    // middleware) and the Program.cs binding-failure factory hookup. Reordering or
    // unwiring would pass every other test in this suite, so these read the source and
    // fail on a swap.
    public class ManagementPipelineOrderTests
    {
        private const string RelativeSource = "src/server/HSMServer/Extensions/ApplicationServiceExtensions.cs";


        private static string ReadPipelineSource() => ReadSource(RelativeSource);

        private static string ReadSource(string relativeSource)
        {
            var directory = new DirectoryInfo(System.AppContext.BaseDirectory);

            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
                directory = directory.Parent;

            var source = directory is null ? null : Path.Combine(directory.FullName, relativeSource.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(source is not null && File.Exists(source),
                $"Cannot locate {relativeSource} (resolved: {source ?? "<none>"}, started from {System.AppContext.BaseDirectory}) — the pipeline-order pin needs the repo checkout");

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

            AssertInOrder(body, pinnedOrder);
        }

        [Fact]
        public void ExceptionJsonMiddleware_SitsBetweenGlobalHandlerAndLogging()
        {
            // The JSON-500 property of the error contract (#1353): the /api exception
            // handler must catch AFTER LoggingExceptionMiddleware (which logs first and
            // rethrows) but BEFORE the global /Error handler (which renders Razor) —
            // only then do /api paths get JSON while every other path keeps the error
            // page, with the failure logged either way.
            var body = ExtractConfigureMiddlewareBody(ReadPipelineSource());

            AssertInOrder(body,
            [
                "UseExceptionHandler",
                "ApiExceptionJsonMiddleware",
                "LoggingExceptionMiddleware",
                "UseRouting",
            ]);
        }

        [Fact]
        public void SwaggerGate_IsRegisteredBeforeTheSwaggerMiddleware()
        {
            // The port gate is correct only ABOVE UseSwagger/UseSwaggerUI; below them
            // the doc is served before the gate runs, silently republishing the
            // management map on the sensor port while the gate's own unit tests stay
            // green (review round 2 on #1366).
            var body = ExtractConfigureMiddlewareBody(ReadPipelineSource());

            AssertInOrder(body,
            [
                "SwaggerSitePortOnlyMiddleware",
                "UseSwagger",
                "UseSwaggerUI",
            ]);
        }

        [Fact]
        public void BindingFailureFactory_IsWiredIntoApiBehaviorOptions()
        {
            // The other registration the contract's unit tests cannot see: the
            // [ApiController] automatic-400 route must wrap — not replace — the
            // framework default (Program.cs, not ConfigureMiddleware), so non-
            // management ApiControllers keep their exact previous wire shape.
            var source = ReadSource("src/server/HSMServer/Program.cs");

            Assert.Contains("InvalidModelStateResponseFactory =", source);
            Assert.Contains("ManagementApiErrors.WrapBindingFailureFactory(options.InvalidModelStateResponseFactory)", source);
        }


        private static void AssertInOrder(string body, string[] markers)
        {
            var lastIndex = -1;

            foreach (var marker in markers)
            {
                var index = body.IndexOf(marker, System.StringComparison.Ordinal);

                Assert.True(index >= 0, $"{marker} is not registered in ConfigureMiddleware");
                Assert.True(index > lastIndex, $"{marker} must come after the previous middleware in the pinned order");

                lastIndex = index;
            }
        }

        // The middleware block lives in ConfigureMiddleware; ordering is only meaningful
        // within that method, not across the whole file. Comment lines are stripped so
        // the markers match REGISTRATIONS, not a comment that happens to quote one.
        private static string ExtractConfigureMiddlewareBody(string source)
        {
            var start = source.IndexOf("ConfigureMiddleware", System.StringComparison.Ordinal);

            Assert.True(start >= 0, "ConfigureMiddleware not found");

            var end = source.IndexOf("InitStorages", start, System.StringComparison.Ordinal);

            return string.Join("\n", source[start..end]
                .Split('\n')
                .Where(line => !line.TrimStart().StartsWith("//")));
        }
    }
}
