using System;
using System.Collections.Generic;
using HSMServer.Filters;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.ServiceExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HSMServer.Core.Tests.Filters
{
    // #1675: the dry-run header is the safety switch of the journaled writes
    // (sensor deletion above all — it has no undo). A value that is not a
    // boolean must fail CLOSED: 400 validation_failed under the header's field
    // key, before the write runs. Absent/empty = real write, "true"/"false"
    // any casing = their meanings, and the rule applies to the /api/v1
    // management surface only.
    public class DryRunHeaderFilterTests
    {
        [Theory]
        [InlineData("maybe")]
        [InlineData("True1")]
        [InlineData("yes")]
        [InlineData("1")]
        [InlineData("0")]
        public void AMalformedHeader_FailsClosedWith400BeforeTheAction(string raw)
        {
            var context = ExecutingContext("/api/v1/sensors/00000000-0000-0000-0000-000000000000", raw);

            new DryRunHeaderFilter().OnActionExecuting(context);

            var result = Assert.IsType<ObjectResult>(context.Result);
            Assert.Equal(400, result.StatusCode);

            var error = Assert.IsType<ManagementApiErrorDto>(result.Value);
            Assert.Equal(ManagementApiErrors.ValidationFailedCode, error.Error);

            var details = Assert.IsType<Dictionary<string, object>>(error.Details);
            Assert.Contains(ChangeSetHeaders.DryRun, details.Keys);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("true")]
        [InlineData("True")]
        [InlineData("TRUE")]
        [InlineData("false")]
        [InlineData("False")]
        public void AValidOrAbsentHeader_ReachesTheAction(string raw)
        {
            var context = ExecutingContext("/api/v1/sensors/00000000-0000-0000-0000-000000000000", raw);

            new DryRunHeaderFilter().OnActionExecuting(context);

            Assert.Null(context.Result);
        }

        [Fact]
        public void OutsideTheManagementApi_TheHeaderIsNotTheFilterSBusiness()
        {
            // The sensor-data API and the web controllers do not honour the
            // header; a stray value there is not this filter's contract.
            var context = ExecutingContext("/api/sensors/list", "maybe");

            new DryRunHeaderFilter().OnActionExecuting(context);

            Assert.Null(context.Result);
        }

        [Fact]
        public void WriteOptions_StillParseTheAcceptedForms()
        {
            Assert.True(DryRunOf("true"));
            Assert.True(DryRunOf("True"));
            Assert.False(DryRunOf("false"));
            Assert.False(DryRunOf(null));
            Assert.False(DryRunOf(""));
        }

        // The unit tests above call the filter directly; the 400 only happens
        // on a real request if the filter is REGISTERED. This pins the server's
        // own MVC registration (ApplicationServiceExtensions.AddHsmMvc — the
        // method Program.cs calls) so dropping or moving it fails a test
        // instead of quietly re-enabling the irreversible delete (#1675).
        [Fact]
        public void TheServerMvcRegistration_InstallsTheFilterOnThePipeline()
        {
            var services = new ServiceCollection();

            services.AddHsmMvc();

            var filters = services.BuildServiceProvider()
                .GetRequiredService<IOptions<MvcOptions>>()
                .Value.Filters;

            Assert.Contains(filters, filter =>
                filter is TypeFilterAttribute typeFilter && typeFilter.ImplementationType == typeof(DryRunHeaderFilter));
        }


        private static bool DryRunOf(string raw)
        {
            var http = new DefaultHttpContext();
            http.Request.Headers[ChangeSetHeaders.DryRun] = raw;

            return http.Request.WriteOptions().DryRun;
        }

        private static ActionExecutingContext ExecutingContext(string path, string raw)
        {
            var http = new DefaultHttpContext();
            http.Request.Path = path;

            if (raw is not null)
                http.Request.Headers[ChangeSetHeaders.DryRun] = raw;

            var actionContext = new ActionContext(http, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());

            return new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), new object());
        }
    }
}
