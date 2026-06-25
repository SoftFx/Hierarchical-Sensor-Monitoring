using System;
using System.Linq;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    public class DefaultSlackDestinationsRoundTripTests
    {
        [Fact]
        public void ProductModel_ToEntity_PreservesDefaultSlackDestinationsSettings()
        {
            var product = new ProductModel("parent");
            var slackDestination = Guid.NewGuid();

            product.Settings.DefaultSlackDestinations.TrySetValue(new PolicyDestinationSettings(
                new PolicyDestinationSettingsEntity
                {
                    Chats = new() { { slackDestination.ToString(), "ops-slack" } },
                    Mode = (byte)DefaultChatsMode.Custom,
                }));

            var entity = product.ToEntity();

            Assert.NotNull(entity.DefaultSlackDestinationsSettings);
            Assert.Equal((byte)DefaultChatsMode.Custom, entity.DefaultSlackDestinationsSettings.Mode);
            Assert.Contains(slackDestination.ToString(), entity.DefaultSlackDestinationsSettings.Chats.Keys);
        }

        [Fact]
        public void ProductModel_FromEntity_LoadsDefaultSlackDestinationsSettings()
        {
            var slackDestination = Guid.NewGuid();
            var entity = new ProductEntity
            {
                Id = Guid.NewGuid().ToString(),
                AuthorId = Guid.NewGuid().ToString(),
                ParentProductId = Guid.NewGuid().ToString(),
                DisplayName = "from-entity",
                State = (int)ProductState.FullAccess,
                CreationDate = DateTime.UtcNow.Ticks,
                DefaultSlackDestinationsSettings = new PolicyDestinationSettingsEntity
                {
                    Chats = new() { { slackDestination.ToString(), "ops-slack" } },
                    Mode = (byte)DefaultChatsMode.Custom,
                },
            };

            var product = new ProductModel(entity);

            Assert.Equal(DefaultChatsMode.Custom, product.Settings.DefaultSlackDestinations.CurValue.Mode);
            Assert.Contains(slackDestination, product.Settings.DefaultSlackDestinations.CurValue.Chats.Keys);
        }

        [Fact]
        public void ProductModel_RoundTripsDefaultSlackDestinationsThroughEntity()
        {
            var product = new ProductModel("parent");
            var slackDestination1 = Guid.NewGuid();
            var slackDestination2 = Guid.NewGuid();

            product.Settings.DefaultSlackDestinations.TrySetValue(new PolicyDestinationSettings(
                new PolicyDestinationSettingsEntity
                {
                    Chats = new()
                    {
                        { slackDestination1.ToString(), "ops" },
                        { slackDestination2.ToString(), "incidents" },
                    },
                    Mode = (byte)DefaultChatsMode.Custom,
                }));

            var roundTripped = new ProductModel(product.ToEntity());

            var loaded = roundTripped.Settings.DefaultSlackDestinations.CurValue;
            Assert.Equal(2, loaded.Chats.Count);
            Assert.Contains(slackDestination1, loaded.Chats.Keys);
            Assert.Contains(slackDestination2, loaded.Chats.Keys);
        }

        [Fact]
        public void ProductModel_LegacyEntityWithoutDefaultSlackDestinations_DefaultsToNotInitialized()
        {
            var entity = new ProductEntity
            {
                Id = Guid.NewGuid().ToString(),
                AuthorId = Guid.NewGuid().ToString(),
                ParentProductId = Guid.NewGuid().ToString(),
                DisplayName = "legacy",
                State = (int)ProductState.FullAccess,
                CreationDate = DateTime.UtcNow.Ticks,
                DefaultSlackDestinationsSettings = null,
            };

            var product = new ProductModel(entity);

            Assert.Equal(DefaultChatsMode.NotInitialized, product.Settings.DefaultSlackDestinations.CurValue.Mode);
            Assert.Empty(product.Settings.DefaultSlackDestinations.CurValue.Chats);
        }

        [Fact]
        public void ProductUpdate_DefaultSlackDestinations_AppliedToSettings()
        {
            var product = new ProductModel("parent");
            var slackDestination = Guid.NewGuid();

            var update = new ProductUpdate
            {
                Id = product.Id,
                DefaultSlackDestinations = new PolicyDestinationSettings(
                    new PolicyDestinationSettingsEntity
                    {
                        Chats = new() { { slackDestination.ToString(), "ops" } },
                        Mode = (byte)DefaultChatsMode.Custom,
                    }),
                Initiator = InitiatorInfo.AsUser("test"),
            };

            product.Update(update);

            Assert.Equal(DefaultChatsMode.Custom, product.Settings.DefaultSlackDestinations.CurValue.Mode);
            Assert.Contains(slackDestination, product.Settings.DefaultSlackDestinations.CurValue.Chats.Keys);
        }
    }
}
