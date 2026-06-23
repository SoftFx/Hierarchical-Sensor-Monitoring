using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Model.Agent;
using System;
using Xunit;

namespace HSMServer.Core.Tests
{
    // Deterministic, Docker-free coverage of the per-product download decisions (epic #1167, W9):
    // which access key gets baked into the bundle, and which (address, port) the config.json points at.
    // The full Windows install -> data -> stop run is a manual smoke (cross-OS: the agent installs on
    // Windows, the server runs as a Linux container).
    public class AgentKeySelectorTests
    {
        private static AccessKeyModel Key(ProductModel product, string name, KeyPermissions perms, bool expired = false)
        {
            return new AccessKeyModel(new AccessKeyEntity
            {
                Id = Guid.NewGuid().ToString(),
                ProductId = product.Id.ToString(),
                State = (byte)KeyState.Active,
                Permissions = (long)perms,
                DisplayName = name,
                CreationTime = DateTime.UtcNow.Ticks,
                ExpirationTime = (expired ? DateTime.UtcNow.AddDays(-1) : DateTime.MaxValue).Ticks,
            });
        }

        [Fact]
        public void Select_PrefersTheValidDefaultKey()
        {
            var product = new ProductModel("P");
            var other = Key(product, "Other", AgentKeySelector.AgentPermissions);
            var def = AccessKeyModel.BuildDefault(product);
            product.AccessKeys.TryAdd(other.Id, other);
            product.AccessKeys.TryAdd(def.Id, def);

            var selected = AgentKeySelector.Select(product);

            Assert.NotNull(selected);
            Assert.Equal(def.Id, selected.Id);
            Assert.Equal(CommonConstants.DefaultAccessKey, selected.DisplayName);
        }

        [Fact]
        public void Select_ReturnsNull_WhenProductHasNoKeys()
        {
            Assert.Null(AgentKeySelector.Select(new ProductModel("P")));
        }

        [Fact]
        public void Select_FallsBackToAnyValidKey_WhenNoDefault()
        {
            var product = new ProductModel("P");
            var custom = Key(product, "Custom", AgentKeySelector.AgentPermissions);
            product.AccessKeys.TryAdd(custom.Id, custom);

            Assert.Equal(custom.Id, AgentKeySelector.Select(product).Id);
        }

        [Fact]
        public void Select_SkipsExpiredDefault_ForAValidKey()
        {
            var product = new ProductModel("P");
            var expiredDefault = Key(product, CommonConstants.DefaultAccessKey, AgentKeySelector.AgentPermissions, expired: true);
            var valid = Key(product, "Custom", AgentKeySelector.AgentPermissions);
            product.AccessKeys.TryAdd(expiredDefault.Id, expiredDefault);
            product.AccessKeys.TryAdd(valid.Id, valid);

            Assert.Equal(valid.Id, AgentKeySelector.Select(product).Id);
        }

        [Fact]
        public void Select_RejectsKeyWithoutSendDataPermission_ButStillReturnsDefaultAsLastResort()
        {
            var product = new ProductModel("P");
            // A read-only key is not valid for the agent; with no better option the named DefaultKey wins.
            var readOnlyDefault = Key(product, CommonConstants.DefaultAccessKey, KeyPermissions.CanReadSensorData);
            product.AccessKeys.TryAdd(readOnlyDefault.Id, readOnlyDefault);

            var selected = AgentKeySelector.Select(product);

            Assert.NotNull(selected);
            Assert.Equal(readOnlyDefault.Id, selected.Id);
        }
    }

    public class AgentConnectionResolverTests
    {
        [Fact]
        public void Resolve_UsesExternalUrlWithExplicitPort()
        {
            var (address, port) = AgentConnectionResolver.Resolve("https://hsm.example.com:9000", 44330, "http", "ignored");

            Assert.Equal("https://hsm.example.com", address);
            Assert.Equal(9000, port);
        }

        [Fact]
        public void Resolve_FallsBackToSensorPort_WhenExternalUrlHasNoPort()
        {
            var (address, port) = AgentConnectionResolver.Resolve("https://hsm.example.com", 44330, "http", "ignored");

            Assert.Equal("https://hsm.example.com", address);
            Assert.Equal(44330, port);
        }

        [Fact]
        public void Resolve_AssumesHttps_WhenExternalUrlHasNoScheme()
        {
            var (address, port) = AgentConnectionResolver.Resolve("hsm.example.com:8443", 44330, "http", "ignored");

            Assert.Equal("https://hsm.example.com", address);
            Assert.Equal(8443, port);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Resolve_FallsBackToRequestHost_WhenExternalUrlBlank(string external)
        {
            var (address, port) = AgentConnectionResolver.Resolve(external, 44330, "https", "req-host");

            Assert.Equal("https://req-host", address);
            Assert.Equal(44330, port);
        }
    }
}
