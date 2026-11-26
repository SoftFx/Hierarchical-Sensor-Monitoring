using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Controllers;
using HSMServer.Model.FileSensors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    /// <summary>The model a render request draws, one per test host.</summary>
    public sealed class FileAccordionsModelHolder
    {
        public object Model { get; set; }

        public string View { get; set; } = "~/Views/Home/_FileAccordions.cshtml";
    }


    // Renders a partial of the server with its compiled view, as the Home controller does.
    public sealed class FileAccordionsRenderController : Controller
    {
        [HttpGet("/test/render")]
        public IActionResult Render([FromServices] FileAccordionsModelHolder holder) => PartialView(holder.View, holder.Model);
    }


    // #1886: the Files tab as the server renders it, for every state of a large file and both kinds, with the password
    // notice for each role, and a client's strings (name, extension, application, host) only ever as encoded text and
    // data attributes. Playwright cannot seed large values until the file connection (#1884) is in the branch, so the
    // state matrix is held here.
    public sealed class FileAccordionsRenderTests : IAsyncLifetime
    {
        private const string Hostile = "<img src=x onerror=alert(1)>";
        private const string HostileExtension = "x');alert(1)//";

        private readonly FileAccordionsModelHolder _holder = new();
        private IHost _host;
        private HttpClient _client;


        public async Task InitializeAsync()
        {
            _host = await Host.CreateDefaultBuilder()
                .UseEnvironment(Environments.Development)
                .ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();
                    web.ConfigureServices(services =>
                    {
                        services.AddControllersWithViews()
                                .AddApplicationPart(typeof(HomeController).Assembly)
                                .AddApplicationPart(typeof(FileAccordionsRenderController).Assembly);
                        services.AddSingleton(_holder);
                    });
                    web.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapControllers());
                    });
                })
                .StartAsync();

            _client = _host.GetTestClient();
        }

        public async Task DisposeAsync()
        {
            _client?.Dispose();

            if (_host is not null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
        }


        public static IEnumerable<object[]> States() =>
        [
            [LargeFileRowState.WaitingForPassword, "waiting for the product's password", FileSensorPageService.SecureKind, false],
            [LargeFileRowState.Packing, "being packed", FileSensorPageService.NotSecureKind, false],
            [LargeFileRowState.WaitingForRoom, "being packed (waiting for disk space)", FileSensorPageService.SecureKind, false],
            [LargeFileRowState.PackingFailed, "packing failed", FileSensorPageService.SecureKind, false],
            [LargeFileRowState.Ready, "ready", FileSensorPageService.NotSecureKind, true],
            [LargeFileRowState.Missing, "file is missing", null, false],
        ];

        [Theory]
        [MemberData(nameof(States))]
        public async Task A_large_row_shows_its_state_and_kind_and_a_download_only_when_ready(LargeFileRowState state, string text, string kind, bool ready)
        {
            var html = await RenderAsync(Page(LargeRow(state, text, kind, ready ? "/LargeFiles/Download/f/a" : null)));

            Assert.Contains($">{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(text)}<", html);
            Assert.Contains($"data-state='{state}'", html);
            Assert.Equal(kind is not null, html.Contains("large-file-kind"));

            if (kind is not null)
                Assert.Contains($">{kind}<", html);

            Assert.Equal(ready, html.Contains("href='/LargeFiles/Download/f/a'"));

            // A large value never gets the preview nor the content download of an ordinary file.
            Assert.DoesNotContain("file-row-view", html);
            Assert.DoesNotContain("file-row-download", html);
            Assert.Contains("data-preview='false'", html);

            // Only a file being packed makes the tab read the list again; a file waiting for a password, for room or after a
            // failed build changes rarely and is not polled.
            Assert.Contains($"data-live='{(state == LargeFileRowState.Packing ? "true" : "false")}'", html);
        }

        [Fact]
        public async Task An_upload_shows_its_percent_kind_and_origin()
        {
            var html = await RenderAsync(new FileValuesPageViewModel
            {
                EncodedId = "s",
                Uploads =
                [
                    new LargeFileUploadRowViewModel
                    {
                        Name = "app.dmp", Size = "1.00 GB", Kind = FileSensorPageService.SecureKind, State = LargeFileRowState.Uploading,
                        StateText = "uploading, 42%", Percent = 42, Application = "TTS", Host = "10.0.0.7",
                    },
                ],
                MoreUploads = 3,
            });

            Assert.Contains(">uploading, 42%<", html);
            Assert.Contains("width: 42%", html);
            Assert.Contains(">TTS<", html);
            Assert.Contains(">10.0.0.7<", html);
            Assert.Contains("and 3 more uploads in progress", html);
        }

        [Theory]
        [InlineData(LargeFileRowState.Uploading, true)]
        [InlineData(LargeFileRowState.Checking, true)]
        [InlineData(LargeFileRowState.WaitingForCollector, false)]
        public async Task An_upload_is_polled_while_it_moves_and_a_stalled_one_says_when_it_goes(LargeFileRowState state, bool live)
        {
            var html = await RenderAsync(new FileValuesPageViewModel
            {
                EncodedId = "s",
                Uploads =
                [
                    new LargeFileUploadRowViewModel
                    {
                        Name = "a.dmp", Size = "1 B", State = state, StateText = "x",
                        LastPieceUtc = new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc), ExpiresUtc = new DateTime(2026, 12, 3, 10, 0, 0, DateTimeKind.Utc),
                    },
                ],
            });

            Assert.Contains($"data-live=\"{(live ? "true" : "false")}\"", html);
            Assert.Equal(state == LargeFileRowState.WaitingForCollector, html.Contains("large-file-stalled"));
        }

        [Fact]
        public async Task The_downloads_of_a_file_say_when_the_log_cannot_be_read()
        {
            _holder.View = "~/Views/Home/_LargeFileDownloads.cshtml";

            var html = await RenderAsync(new LargeFileDownloadsViewModel { IsUnavailable = true });

            Assert.Contains("The download log cannot be read now.", html);
        }

        [Fact]
        public async Task The_password_notice_links_the_settings_only_for_who_may_set_it()
        {
            var viewer = await RenderAsync(new FileValuesPageViewModel { EncodedId = "s", ShowPasswordMissing = true });
            var manager = await RenderAsync(new FileValuesPageViewModel { EncodedId = "s", ShowPasswordMissing = true, PasswordSettingsUrl = "/Product/EditProduct?Product=p" });
            var none = await RenderAsync(new FileValuesPageViewModel { EncodedId = "s" });

            Assert.Contains("the product has no dump password yet", viewer);
            Assert.DoesNotContain("Set the dump password", viewer);
            Assert.Contains("href=\"/Product/EditProduct?Product=p\"", manager);
            Assert.DoesNotContain("large-file-password-missing", none);
        }

        [Fact]
        public async Task A_clients_strings_are_encoded_text_and_data_never_script_or_markup()
        {
            var ordinary = new FileValueRowViewModel
            {
                TimeTicks = 638_000_000_000_000_000, Time = new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc), Name = $"{Hostile}.txt",
                Extension = HostileExtension, Size = "1 B", Status = SensorStatus.Ok, Comment = Hostile,
            };

            var large = LargeRow(LargeFileRowState.Ready, "ready", FileSensorPageService.SecureKind, "/LargeFiles/Download/f/a");
            large = Copy(large, name: $"{Hostile}.dmp", application: Hostile, host: $"\"{Hostile}");

            var html = await RenderAsync(Page(ordinary, large));

            Assert.DoesNotContain("<img", html);
            Assert.DoesNotContain("onerror=alert", html.Replace("onerror=alert(1)&gt;", string.Empty));
            Assert.DoesNotContain("onclick", html);
            Assert.DoesNotContain("<script", html);
            Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html);
            Assert.Contains("data-extension='x&#x27;);alert(1)//'", html);
            Assert.All(System.Text.RegularExpressions.Regex.Matches(html, "\\bid=['\"]([^'\"]*)['\"]").Select(m => m.Groups[1].Value),
                id => Assert.Matches("^[A-Za-z0-9_-]+$", id));
        }

        [Fact]
        public async Task The_downloads_of_a_file_are_encoded_text()
        {
            _holder.View = "~/Views/Home/_LargeFileDownloads.cshtml";

            var html = await RenderAsync(new LargeFileDownloadsViewModel
            {
                Downloads = [new LargeFileDownloadFacts(new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc), $"{Hostile} (token x)", "API")],
            });

            Assert.DoesNotContain("<img", html);
            Assert.Contains("&lt;img", html);
            Assert.Contains("API", html);

            var empty = await RenderAsync(new LargeFileDownloadsViewModel());
            Assert.Contains("Nobody has downloaded this file.", empty);
        }


        private async Task<string> RenderAsync(object model)
        {
            _holder.Model = model;

            using var response = await _client.GetAsync("/test/render");

            Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

            return await response.Content.ReadAsStringAsync();
        }

        private static FileValuesPageViewModel Page(params FileValueRowViewModel[] rows) => new() { EncodedId = "s", Rows = rows };

        private static FileValueRowViewModel LargeRow(LargeFileRowState state, string text, string kind, string download) => new()
        {
            TimeTicks = 638_000_000_000_000_001,
            Time = new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc),
            Name = "app.dmp",
            Extension = "dmp",
            Size = "8.00 GB",
            Status = SensorStatus.Ok,
            IsLarge = true,
            Kind = kind,
            LargeState = state,
            StateText = text,
            DownloadUrl = download,
            SourceModified = new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Utc),
            FileId = Guid.NewGuid(),
        };

        private static FileValueRowViewModel Copy(FileValueRowViewModel row, string name, string application, string host) => new()
        {
            TimeTicks = row.TimeTicks, Time = row.Time, Name = name, Extension = row.Extension, Size = row.Size, Status = row.Status,
            IsLarge = row.IsLarge, Kind = row.Kind, LargeState = row.LargeState, StateText = row.StateText, DownloadUrl = row.DownloadUrl,
            SourceModified = row.SourceModified, FileId = row.FileId, Application = application, Host = host, ShowsDownloads = true,
        };
    }
}
