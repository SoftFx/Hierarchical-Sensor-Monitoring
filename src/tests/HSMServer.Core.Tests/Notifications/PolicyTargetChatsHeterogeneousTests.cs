using System;
using System.Collections.Generic;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Core.Tests.Infrastructure;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    public class PolicyTargetChatsHeterogeneousTests
    {
        [Fact]
        [Trait("Category", "Policy TargetChats heterogeneous FromParent")]
        public void TargetChats_FromParent_ResolvesBothTelegramAndSlackParentDefaults()
        {
            var parent = new ProductModel("parent");
            var child = new ProductModel("child");
            parent.AddSubProduct(child);

            var telegramChat = Guid.NewGuid();
            var slackDestination = Guid.NewGuid();

            parent.Settings.DefaultChats.TrySetValue(new PolicyDestinationSettings(
                new PolicyDestinationSettingsEntity
                {
                    Chats = new() { { telegramChat.ToString(), "tg-chat" } },
                    Mode = (byte)DefaultChatsMode.Custom,
                }));

            parent.Settings.DefaultSlackDestinations.TrySetValue(new PolicyDestinationSettings(
                new PolicyDestinationSettingsEntity
                {
                    Chats = new() { { slackDestination.ToString(), "slack-dest" } },
                    Mode = (byte)DefaultChatsMode.Custom,
                }));

            var sensor = AddIntegerSensor(child);

            var policy = BuildPolicy(sensor, PolicyDestinationMode.FromParent);

            var targetChats = policy.TargetChats;

            Assert.Contains(telegramChat, targetChats.Chats.Keys);
            Assert.Contains(slackDestination, targetChats.Chats.Keys);
        }

        [Fact]
        [Trait("Category", "Policy TargetChats heterogeneous FromParent")]
        public void TargetChats_FromParent_WalksGrandparentForBothChannels()
        {
            var grandparent = new ProductModel("grandparent");
            var parent = new ProductModel("parent");
            var child = new ProductModel("child");
            grandparent.AddSubProduct(parent);
            parent.AddSubProduct(child);

            var grandparentTelegramChat = Guid.NewGuid();
            var grandparentSlackDestination = Guid.NewGuid();

            grandparent.Settings.DefaultChats.TrySetValue(new PolicyDestinationSettings(
                new PolicyDestinationSettingsEntity
                {
                    Chats = new() { { grandparentTelegramChat.ToString(), "tg-grandparent" } },
                    Mode = (byte)DefaultChatsMode.Custom,
                }));

            grandparent.Settings.DefaultSlackDestinations.TrySetValue(new PolicyDestinationSettings(
                new PolicyDestinationSettingsEntity
                {
                    Chats = new() { { grandparentSlackDestination.ToString(), "slack-grandparent" } },
                    Mode = (byte)DefaultChatsMode.Custom,
                }));

            parent.Settings.DefaultChats.TrySetValue(new PolicyDestinationSettings(
                new PolicyDestinationSettingsEntity
                {
                    Mode = (byte)DefaultChatsMode.FromParent,
                }));

            parent.Settings.DefaultSlackDestinations.TrySetValue(new PolicyDestinationSettings(
                new PolicyDestinationSettingsEntity
                {
                    Mode = (byte)DefaultChatsMode.FromParent,
                }));

            var sensor = AddIntegerSensor(child);
            var policy = BuildPolicy(sensor, PolicyDestinationMode.FromParent);

            var targetChats = policy.TargetChats;

            Assert.Contains(grandparentTelegramChat, targetChats.Chats.Keys);
            Assert.Contains(grandparentSlackDestination, targetChats.Chats.Keys);
        }

        [Fact]
        [Trait("Category", "Policy TargetChats heterogeneous FromParent")]
        public void TargetChats_CustomMode_OnlyReturnsExplicitlySelectedChats()
        {
            var parent = new ProductModel("parent");
            var child = new ProductModel("child");
            parent.AddSubProduct(child);

            var parentTelegramChat = Guid.NewGuid();
            var parentSlackDestination = Guid.NewGuid();

            parent.Settings.DefaultChats.TrySetValue(new PolicyDestinationSettings(
                new PolicyDestinationSettingsEntity
                {
                    Chats = new() { { parentTelegramChat.ToString(), "tg-parent" } },
                    Mode = (byte)DefaultChatsMode.Custom,
                }));

            parent.Settings.DefaultSlackDestinations.TrySetValue(new PolicyDestinationSettings(
                new PolicyDestinationSettingsEntity
                {
                    Chats = new() { { parentSlackDestination.ToString(), "slack-parent" } },
                    Mode = (byte)DefaultChatsMode.Custom,
                }));

            var explicitChat = Guid.NewGuid();
            var sensor = AddIntegerSensor(child);

            var policy = BuildPolicy(sensor, PolicyDestinationMode.Custom);
            policy.TryUpdate(new PolicyUpdate
            {
                Id = policy.Id,
                Conditions = [],
                Destination = new PolicyDestinationUpdate(
                    new Dictionary<Guid, string> { { explicitChat, "explicit" } },
                    PolicyDestinationMode.Custom),
            }, out _);

            var targetChats = policy.TargetChats;

            Assert.Contains(explicitChat, targetChats.Chats.Keys);
            Assert.DoesNotContain(parentTelegramChat, targetChats.Chats.Keys);
            Assert.DoesNotContain(parentSlackDestination, targetChats.Chats.Keys);
        }


        private static BaseSensorModel AddIntegerSensor(ProductModel product)
        {
            var sensorEntity = EntitiesFactory.BuildSensorEntity(name: "sensor", parent: product.Id.ToString(), type: (byte)SensorType.Integer);
            var scheduleProvider = new Mock<IAlertScheduleProvider>();
            var sensor = new IntegerSensorModel(sensorEntity, null, scheduleProvider.Object);
            product.AddSensor(sensor);

            return sensor;
        }

        private static Policy BuildPolicy(BaseSensorModel sensor, PolicyDestinationMode mode)
        {
            var policy = Policy.BuildPolicy((byte)SensorType.Integer);
            policy.TryUpdate(new PolicyUpdate
            {
                Id = Guid.NewGuid(),
                Conditions = [],
                Destination = new PolicyDestinationUpdate(mode),
            }, out _, sensor);

            return policy;
        }
    }
}
