using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Core.LargeFiles.DumpPasswords;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Model.Authentication;
using HSMServer.Model.LargeFiles;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // The dump password section of a product's settings (#1885): Manager of the ROOT product or admin only, POST +
    // antiforgery, never cached; values only in the JSON answers of Show and Generate, never in the section model or a log.
    public sealed class DumpPasswordControllerTests
    {
        private const string CurrentValue = "Current-Dump-Password-0123456789";
        private const string ReplacedValue = "Replaced-Dump-Password-987654321";

        private delegate void OutProductCallback(Guid id, out ProductModel product);

        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IDumpPasswordService> _passwords = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<IServerConfig> _config = new();
        private readonly RecordingLogger _log = new();

        private readonly ProductModel _root = new(EntitiesFactory.BuildProductEntity(name: "root") with { Id = Guid.NewGuid().ToString() });
        private readonly ProductModel _nested = new(EntitiesFactory.BuildProductEntity(name: "nested") with { Id = Guid.NewGuid().ToString() });

        private static readonly DateTime Retired = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);


        public DumpPasswordControllerTests()
        {
            _root.AddSubProduct(_nested);

            foreach (var product in new[] { _root, _nested })
                _cache.Setup(c => c.TryGetProduct(product.Id, out It.Ref<ProductModel>.IsAny))
                    .Callback(new OutProductCallback((Guid _, out ProductModel p) => p = product))
                    .Returns(true);

            var versions = new List<DumpPasswordVersion>
            {
                new(1, ReplacedValue, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), "managerA", Retired),
                new(2, CurrentValue, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), "admin", null),
            };

            _passwords.Setup(p => p.HasPassword(_root.Id)).Returns(true);
            _passwords.Setup(p => p.GetVersions(_root.Id)).Returns(versions);
            _passwords.Setup(p => p.GetCurrent(_root.Id)).Returns(versions[1]);
            _passwords.Setup(p => p.Generate()).Returns("AbCdEfGhIjKlMnOpQrStUv12");
            _passwords.Setup(p => p.SetPassword(_root.Id, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<InitiatorInfo>()))
                .Returns(new DumpPasswordSetResult(DumpPasswordSetOutcome.Set, 3));

            _config.Setup(c => c.LargeFileArchives).Returns(new LargeFileArchivesConfig { PasswordRetentionDays = 30 });
        }


        private DumpPasswordController CreateController(User user) =>
            new(_users.Object, new DumpPasswordPageService(_cache.Object, _passwords.Object, _config.Object), _log)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
            };

        private User Manager() => new("managerA") { ProductsRoles = { (_root.Id, ProductRoleEnum.ProductManager) } };

        private User Viewer() => new("viewerA") { ProductsRoles = { (_root.Id, ProductRoleEnum.ProductViewer) } };

        private static int StatusOf(IActionResult result) => result switch
        {
            JsonResult json => json.StatusCode ?? 200,
            StatusCodeResult code => code.StatusCode,
            _ => throw new InvalidOperationException(result.GetType().Name),
        };

        private static string Json(IActionResult result) => JsonSerializer.Serialize(((JsonResult)result).Value);


        [Theory]
        [InlineData(nameof(DumpPasswordController.Show))]
        [InlineData(nameof(DumpPasswordController.Generate))]
        [InlineData(nameof(DumpPasswordController.Set))]
        public void EveryAction_IsPostOnly_WithAntiforgery(string action)
        {
            var method = typeof(DumpPasswordController).GetMethod(action, BindingFlags.Public | BindingFlags.Instance);

            Assert.True(method.IsDefined(typeof(HttpPostAttribute), inherit: false));
            Assert.False(method.IsDefined(typeof(HttpGetAttribute), inherit: false));
            Assert.True(method.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), inherit: false), $"{action} must require an antiforgery token");
        }

        [Fact]
        public void TheAnswers_AreNeverCached()
        {
            var cache = typeof(DumpPasswordController).GetCustomAttribute<ResponseCacheAttribute>();

            Assert.NotNull(cache);
            Assert.True(cache.NoStore);
        }

        [Fact]
        public void AViewer_CannotShowGenerateOrSet()
        {
            var controller = CreateController(Viewer());

            Assert.Equal(403, StatusOf(controller.Show(new DumpPasswordRequest { ProductId = _root.Id })));
            Assert.Equal(403, StatusOf(controller.Generate(new DumpPasswordRequest { ProductId = _root.Id })));
            Assert.Equal(403, StatusOf(controller.Set(new DumpPasswordSetRequest { ProductId = _root.Id, Password = CurrentValue })));

            _passwords.Verify(p => p.SetPassword(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<InitiatorInfo>()), Times.Never);
            _passwords.Verify(p => p.Generate(), Times.Never);
        }

        [Fact]
        public void AnUnknownProduct_Is404()
        {
            var controller = CreateController(new User("admin") { IsAdmin = true });

            Assert.Equal(404, StatusOf(controller.Show(new DumpPasswordRequest { ProductId = Guid.NewGuid() })));
            Assert.Equal(404, StatusOf(controller.Set(new DumpPasswordSetRequest { ProductId = Guid.NewGuid(), Password = CurrentValue })));
        }

        [Fact]
        public void AUserWithNoRoleOnTheProduct_GetsTheSame404AsForAnUnknownProduct()
        {
            var stranger = CreateController(new User("stranger") { ProductsRoles = { (Guid.NewGuid(), ProductRoleEnum.ProductManager) } });

            foreach (var productId in new[] { _root.Id, _nested.Id })
            {
                var answers = new[]
                {
                    (stranger.Show(new DumpPasswordRequest { ProductId = productId }),
                     stranger.Show(new DumpPasswordRequest { ProductId = Guid.NewGuid() })),
                    (stranger.Generate(new DumpPasswordRequest { ProductId = productId }),
                     stranger.Generate(new DumpPasswordRequest { ProductId = Guid.NewGuid() })),
                    (stranger.Set(new DumpPasswordSetRequest { ProductId = productId, Password = CurrentValue }),
                     stranger.Set(new DumpPasswordSetRequest { ProductId = Guid.NewGuid(), Password = CurrentValue })),
                };

                foreach (var (known, unknown) in answers)
                {
                    Assert.Equal(404, StatusOf(known));
                    Assert.Equal(unknown.GetType(), known.GetType());
                    Assert.Equal(StatusOf(unknown), StatusOf(known));
                }
            }

            _passwords.Verify(p => p.SetPassword(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<InitiatorInfo>()), Times.Never);
            _passwords.Verify(p => p.Generate(), Times.Never);
        }

        [Fact]
        public void AManager_SeesEveryKeptValue_NewestFirst_WithTheReplacedOneMarked()
        {
            var result = CreateController(Manager()).Show(new DumpPasswordRequest { ProductId = _root.Id });

            Assert.Equal(200, StatusOf(result));

            using var body = JsonDocument.Parse(Json(result));
            var versions = body.RootElement.GetProperty("versions");

            Assert.Equal(2, versions[0].GetProperty("Version").GetInt32());
            Assert.Equal(CurrentValue, versions[0].GetProperty("Password").GetString());
            Assert.True(versions[0].GetProperty("IsCurrent").GetBoolean());
            Assert.Equal(ReplacedValue, versions[1].GetProperty("Password").GetString());
            Assert.Equal(Retired.AddDays(30), versions[1].GetProperty("KeptUntilUtc").GetDateTime());
        }

        [Fact]
        public void ANestedNode_ResolvesToTheRootProduct()
        {
            var controller = CreateController(Manager());

            Assert.Equal(200, StatusOf(controller.Set(new DumpPasswordSetRequest { ProductId = _nested.Id, Password = CurrentValue })));
            Assert.Equal(403, StatusOf(CreateController(Viewer()).Show(new DumpPasswordRequest { ProductId = _nested.Id })));

            _passwords.Verify(p => p.SetPassword(_root.Id, CurrentValue, false, It.IsAny<InitiatorInfo>()), Times.Once);
            _passwords.Verify(p => p.SetPassword(_nested.Id, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<InitiatorInfo>()), Times.Never);
        }

        [Fact]
        public void AGeneratedPassword_IsSavedAsGenerated_OnlyWhenItLooksGenerated()
        {
            var controller = CreateController(Manager());

            controller.Set(new DumpPasswordSetRequest { ProductId = _root.Id, Password = "AbCdEfGhIjKlMnOpQrStUv12", Generated = true });
            controller.Set(new DumpPasswordSetRequest { ProductId = _root.Id, Password = "short", Generated = true });

            _passwords.Verify(p => p.SetPassword(_root.Id, "AbCdEfGhIjKlMnOpQrStUv12", true, It.IsAny<InitiatorInfo>()), Times.Once);
            _passwords.Verify(p => p.SetPassword(_root.Id, "short", false, It.IsAny<InitiatorInfo>()), Times.Once);
        }

        [Theory]
        [InlineData(DumpPasswordSetOutcome.TooShort, 400, "at least 16 characters")]
        [InlineData(DumpPasswordSetOutcome.NotPrintableAscii, 400, "printable ASCII")]
        [InlineData(DumpPasswordSetOutcome.TooLong, 400, "at most 256 characters")]
        [InlineData(DumpPasswordSetOutcome.ProductUnknown, 404, null)]
        [InlineData(DumpPasswordSetOutcome.NotRootProduct, 404, null)]
        public void EveryRefusedSet_HasItsAnswer(DumpPasswordSetOutcome outcome, int status, string message)
        {
            _passwords.Setup(p => p.SetPassword(_root.Id, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<InitiatorInfo>()))
                .Returns(new DumpPasswordSetResult(outcome, 0));

            var result = CreateController(Manager()).Set(new DumpPasswordSetRequest { ProductId = _root.Id, Password = "x" });

            Assert.Equal(status, StatusOf(result));
            if (message is not null)
                Assert.Contains(message, Json(result));
        }

        [Fact]
        public void APasswordOverTheMaximum_IsRefusedBeforeTheService()
        {
            var result = CreateController(Manager()).Set(new DumpPasswordSetRequest { ProductId = _root.Id, Password = new string('a', DumpPasswordRules.MaxLength + 1) });

            Assert.Equal(400, StatusOf(result));
            Assert.Contains("at most 256 characters", Json(result));
            _passwords.Verify(p => p.SetPassword(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<InitiatorInfo>()), Times.Never);
        }

        [Fact]
        public void ASave_IsLoggedAndJournaledWithoutTheValue()
        {
            InitiatorInfo initiator = null;
            _passwords.Setup(p => p.SetPassword(_root.Id, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<InitiatorInfo>()))
                .Callback((Guid _, string _, bool _, InitiatorInfo who) => initiator = who)
                .Returns(new DumpPasswordSetResult(DumpPasswordSetOutcome.Set, 3));

            var result = CreateController(Manager()).Set(new DumpPasswordSetRequest { ProductId = _root.Id, Password = CurrentValue });

            Assert.Equal(200, StatusOf(result));
            Assert.DoesNotContain(CurrentValue, Json(result));
            Assert.Single(_log.Lines);
            Assert.All(_log.Lines, line => Assert.DoesNotContain(CurrentValue, line));

            // The service writes the journal record from the initiator; nothing of the value goes with it.
            Assert.DoesNotContain(CurrentValue, initiator.ToString());
        }

        [Fact]
        public void TheSectionModel_NeverHoldsAValue()
        {
            var service = new DumpPasswordPageService(_cache.Object, _passwords.Object, _config.Object);

            var managerSection = service.GetSection(Manager(), _nested.Id);
            var viewerSection = service.GetSection(Viewer(), _root.Id);

            Assert.True(managerSection.CanManage);
            Assert.Equal(_root.Id, managerSection.ProductId);
            Assert.Equal([2, 1], managerSection.Versions.Select(version => version.Version));
            Assert.Equal(Retired.AddDays(30), managerSection.Versions[1].KeptUntilUtc);

            Assert.False(viewerSection.CanManage);
            Assert.True(viewerSection.HasPassword);
            Assert.Empty(viewerSection.Versions);

            Assert.Null(service.GetSection(new User("stranger"), _root.Id));

            foreach (var section in new[] { managerSection, viewerSection })
            {
                var json = JsonSerializer.Serialize(section);
                Assert.DoesNotContain(CurrentValue, json);
                Assert.DoesNotContain(ReplacedValue, json);
            }
        }


        private sealed class RecordingLogger : ILogger<DumpPasswordController>
        {
            public List<string> Lines { get; } = [];

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) =>
                Lines.Add($"{formatter(state, exception)} {exception}");
        }
    }
}
