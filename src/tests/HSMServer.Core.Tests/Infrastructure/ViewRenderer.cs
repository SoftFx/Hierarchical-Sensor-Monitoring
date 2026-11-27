using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using HSMServer.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HSMServer.Core.Tests.Infrastructure
{
    /// <summary>
    /// Renders a compiled Razor view of the server to a string, the way the MVC pipeline does, without a
    /// running host: the tests assert on the HTML a browser would receive.
    /// </summary>
    internal static class ViewRenderer
    {
        /// <param name="viewPath">Application-relative path, e.g. <c>~/Views/Tree/_TreeSensor.cshtml</c>.</param>
        /// <param name="model">The view's model.</param>
        /// <param name="user">The signed-in user the view reads from <c>User</c>.</param>
        /// <param name="configure">Registers the services the view injects.</param>
        public static async Task<string> RenderAsync(string viewPath, object model, ClaimsPrincipal user,
            Action<IServiceCollection> configure = null, Action<ViewDataDictionary> viewData = null)
        {
            var services = new ServiceCollection();
            var environment = new TestEnvironment();
            using var listener = new DiagnosticListener("HSMServer.Core.Tests.Views");

            services.AddSingleton<IWebHostEnvironment>(environment);
            services.AddSingleton<IHostEnvironment>(environment);
            services.AddSingleton<DiagnosticSource>(listener);
            services.AddSingleton(listener);
            services.AddLogging();
            services.AddControllersWithViews().AddApplicationPart(typeof(HomeController).Assembly);

            configure?.Invoke(services);

            await using var provider = services.BuildServiceProvider();

            var http = new DefaultHttpContext { RequestServices = provider, User = user };

            // Url.Action and the anchor/form tag helpers need endpoint routing to be "on"; with no endpoints
            // registered they produce empty links, which is all a rendering test needs.
            http.SetEndpoint(new Endpoint(requestDelegate: null, EndpointMetadataCollection.Empty, "view under test"));

            // Partials are looked up by name under the controller's folder: the controller is the one whose
            // folder the view lives in (~/Views/{controller}/...).
            var routeData = new RouteData();
            var segments = viewPath.Split('/');

            if (segments.Length > 2 && segments[1] == "Views")
                routeData.Values["controller"] = segments[2];

            var actionContext = new ActionContext(http, routeData, new ActionDescriptor());

            var found = provider.GetRequiredService<IRazorViewEngine>().GetView(executingFilePath: null, viewPath, isMainPage: false);

            if (!found.Success)
                throw new InvalidOperationException($"View '{viewPath}' was not found. Searched: {string.Join(", ", found.SearchedLocations)}");

            var data = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };

            viewData?.Invoke(data);

            await using var writer = new StringWriter();

            var viewContext = new ViewContext(actionContext, found.View, data,
                new TempDataDictionary(http, provider.GetRequiredService<ITempDataProvider>()), writer, new HtmlHelperOptions());

            await found.View.RenderAsync(viewContext);

            return writer.ToString();
        }

        /// <summary>
        /// HTML content as text: written the way a view writes it, its characters read back as the
        /// browser shows them.
        /// </summary>
        public static string Text(IHtmlContent content)
        {
            using var writer = new StringWriter();

            content.WriteTo(writer, HtmlEncoder.Default);

            return WebUtility.HtmlDecode(writer.ToString());
        }


        private sealed class TestEnvironment : IWebHostEnvironment
        {
            public string ApplicationName { get; set; } = typeof(HomeController).Assembly.GetName().Name;

            public string EnvironmentName { get; set; } = Environments.Production;

            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

            public string WebRootPath { get; set; } = AppContext.BaseDirectory;

            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

            public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
