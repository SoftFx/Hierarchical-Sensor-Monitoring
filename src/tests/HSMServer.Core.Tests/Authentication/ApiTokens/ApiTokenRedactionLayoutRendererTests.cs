using System;
using HSMServer.Authentication;
using HSMServer.Logging;
using NLog;
using NLog.Config;
using NLog.Layouts;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // Sink-level credential redaction (nlog.config wraps message, exception and URL text
    // in ${hsm-redacted}): whatever fragment of free text carried the credential — the
    // formatted message, the exception, or an inner exception logged by another component
    // (the outer ASP.NET Core exception handlers log the raw exception themselves) — the
    // rendered line contains the public token id, never the secret.
    public sealed class ApiTokenRedactionLayoutRendererTests
    {
        public ApiTokenRedactionLayoutRendererTests()
        {
            LogManager.Setup()
                .SetupExtensions(extensions => extensions.RegisterLayoutRenderer<TokenRedactionLayoutRenderer>());
        }

        private static Layout SinkLayout() =>
            Layout.FromString("${hsm-redacted:inner=${message} ${exception:format=tostring}}");

        private static string ValidCredential() =>
            ApiTokenMaterial.FormatToken(new string('A', ApiTokenMaterial.TokenIdLength),
                new string('B', ApiTokenMaterial.SecretLength));

        [Fact]
        public void Wrapper_RedactsCredentialInMessageExceptionAndInnerException()
        {
            var credential = ValidCredential();
            var tokenId = ApiTokenMaterial.TokenIdOf(credential);

            var evt = new LogEventInfo(LogLevel.Error, "test", $"binding failed near {credential}")
            {
                Exception = new InvalidOperationException("outer",
                    new FormatException($"value {credential} is invalid")),
            };

            var rendered = SinkLayout().Render(evt);

            // The full credential and the secret alone are gone (the inner-exception path
            // is exactly where a wrapped exception used to leak it); the public id stays.
            Assert.DoesNotContain(credential, rendered);
            Assert.DoesNotContain(new string('B', ApiTokenMaterial.SecretLength), rendered);
            Assert.Contains(tokenId, rendered);
            Assert.Contains("«redacted»", rendered);
        }

        [Fact]
        public void Wrapper_LeavesCredentialFreeTextUnchanged()
        {
            var evt = new LogEventInfo(LogLevel.Error, "test", "ordinary message")
            {
                Exception = new InvalidOperationException("ordinary failure"),
            };

            Assert.Equal("ordinary message System.InvalidOperationException: ordinary failure",
                SinkLayout().Render(evt));
        }
    }
}
