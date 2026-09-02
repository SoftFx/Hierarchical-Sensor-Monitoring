using HSMServer.Authentication;
using NLog.Config;
using NLog.LayoutRenderers;
using NLog.LayoutRenderers.Wrappers;

namespace HSMServer.Logging
{
    // NLog sink-level credential redaction: wraps a layout fragment (message, exception
    // text, URL) and replaces every hsm_pat_ credential in the rendered text with its
    // public token id (ApiTokenMaterial.Redact). Redaction belongs at the sink: free text
    // carrying the credential can reach a log target through inner exceptions and through
    // components that log the raw exception themselves (the outer ASP.NET Core exception
    // handlers), which no single call site can rewrite before logging.
    [LayoutRenderer("hsm-redacted")]
    public sealed class TokenRedactionLayoutRenderer : WrapperLayoutRendererBase
    {
        protected override string Transform(string text) => ApiTokenMaterial.Redact(text);
    }
}
