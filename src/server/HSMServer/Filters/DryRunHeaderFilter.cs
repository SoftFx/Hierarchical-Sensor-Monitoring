using System.Collections.Generic;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.ChangeSets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HSMServer.Filters
{
    /// <summary>
    /// #1675: the dry-run header is the safety switch of the journaled writes,
    /// and sensor deletion above all is irreversible — a value that is not a
    /// boolean must fail CLOSED, before the write runs. bool.TryParse answers
    /// false for "maybe" or a typo like "True1", which used to turn the
    /// caller's preview request into the real write. An absent or empty
    /// header stays a normal write, and "true"/"false" (any casing) keep
    /// their meanings; anything else non-empty answers the management API's
    /// 400 validation_failed under the header's field key.
    /// </summary>
    public sealed class DryRunHeaderFilter : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (!context.HttpContext.Request.Path.StartsWithSegments("/api/v1"))
                return;

            var raw = context.HttpContext.Request.Headers[ChangeSetHeaders.DryRun].ToString();

            if (raw.Length == 0 || bool.TryParse(raw, out _))
                return;

            context.Result = ManagementApiErrors.Validation(new Dictionary<string, string[]>
            {
                [ChangeSetHeaders.DryRun] =
                    [$"The {ChangeSetHeaders.DryRun} header must be a boolean — \"true\" (preview, nothing applied) or \"false\" (real write), any casing, or absent; '{raw}' is not one."],
            });
        }

        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}
