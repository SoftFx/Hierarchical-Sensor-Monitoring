using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests.Model.Policies
{
    // Pins Policy.GetParentChats (alert delivery) to the same parent-chain resolution as the
    // destination picker UI (DefaultChatViewModel.GetParentChats) and the usage badge
    // (ChatSensorUsageCalculator.ResolveInheritedChats): a single linear walk that stops at
    // the first ancestor whose DefaultChats.IsFromParent is false. See #1330.
    public class PolicyGetParentChatsTests
    {
        [Fact]
        public void NonInheritingMiddleNode_BreaksChain()
        {
            var rootChat = Guid.NewGuid();
            var midChat = Guid.NewGuid();

            var root = BuildProduct(DefaultChatsMode.Custom, (rootChat, "root"));
            var mid = BuildProduct(DefaultChatsMode.Custom, (midChat, "mid")); // NOT FromParent
            root.AddSubProduct(mid);

            var chats = new IntegerPolicy().GetParentChats(mid);

            Assert.Equal(new Dictionary<Guid, string> { [midChat] = "mid" }, chats);
        }

        [Fact]
        public void AllAncestorsFromParent_WalksToRoot()
        {
            var rootChat = Guid.NewGuid();
            var midChat = Guid.NewGuid();
            var leafChat = Guid.NewGuid();

            var root = BuildProduct(DefaultChatsMode.Custom, (rootChat, "root"));
            var mid = BuildProduct(DefaultChatsMode.FromParent, (midChat, "mid"));
            var leaf = BuildProduct(DefaultChatsMode.FromParent, (leafChat, "leaf"));
            root.AddSubProduct(mid);
            mid.AddSubProduct(leaf);

            var chats = new IntegerPolicy().GetParentChats(leaf);

            Assert.Equal(3, chats.Count);
            Assert.Equal("root", chats[rootChat]);
            Assert.Equal("mid", chats[midChat]);
            Assert.Equal("leaf", chats[leafChat]);
        }

        [Fact]
        public void TwoLevelChain_BothFromParent_IncludesBothChats()
        {
            var rootChat = Guid.NewGuid();
            var midChat = Guid.NewGuid();

            var root = BuildProduct(DefaultChatsMode.Custom, (rootChat, "root"));
            var mid = BuildProduct(DefaultChatsMode.FromParent, (midChat, "mid"));
            root.AddSubProduct(mid);

            var chats = new IntegerPolicy().GetParentChats(mid);

            Assert.Equal(2, chats.Count);
            Assert.Equal("root", chats[rootChat]);
            Assert.Equal("mid", chats[midChat]);
        }

        // The regression case from #1330: the walk starts at a node that DOES inherit (leaf,
        // FromParent) and must stop at the first non-inheriting ancestor (mid, Custom). The
        // pre-fix implementation kept walking and also delivered to root's chats — this test
        // fails against it.
        [Fact]
        public void NonInheritingMiddleNode_BreaksChainForFromParentDescendant()
        {
            var rootChat = Guid.NewGuid();
            var midChat = Guid.NewGuid();
            var leafChat = Guid.NewGuid();

            var root = BuildProduct(DefaultChatsMode.Custom, (rootChat, "root"));
            var mid = BuildProduct(DefaultChatsMode.Custom, (midChat, "mid")); // NOT FromParent
            var leaf = BuildProduct(DefaultChatsMode.FromParent, (leafChat, "leaf"));
            root.AddSubProduct(mid);
            mid.AddSubProduct(leaf);

            var chats = new IntegerPolicy().GetParentChats(leaf);

            Assert.Equal(2, chats.Count);
            Assert.Equal("leaf", chats[leafChat]);
            Assert.Equal("mid", chats[midChat]);
            Assert.False(chats.ContainsKey(rootChat)); // old implementation included rootChat
        }

        // Same shape as above, with the breaking ancestor in Empty mode (alerts without
        // notifications) — the mode most likely to surprise operators.
        [Fact]
        public void EmptyModeMiddleNode_BreaksChainForFromParentDescendant()
        {
            var rootChat = Guid.NewGuid();
            var midChat = Guid.NewGuid();
            var leafChat = Guid.NewGuid();

            var root = BuildProduct(DefaultChatsMode.Custom, (rootChat, "root"));
            var mid = BuildProduct(DefaultChatsMode.Empty, (midChat, "mid"));
            var leaf = BuildProduct(DefaultChatsMode.FromParent, (leafChat, "leaf"));
            root.AddSubProduct(mid);
            mid.AddSubProduct(leaf);

            var chats = new IntegerPolicy().GetParentChats(leaf);

            Assert.Equal(2, chats.Count);
            Assert.Equal("leaf", chats[leafChat]);
            Assert.Equal("mid", chats[midChat]);
            Assert.False(chats.ContainsKey(rootChat));
        }

        // Pins the delivery entry point itself: TargetChats gates GetParentChats on
        // Destination.IsFromParentChats and layers explicit Destination.Chats on top.
        [Fact]
        public void TargetChats_FromParentPolicyUnderNonInheritingMiddle_StopsAtMiddle()
        {
            var rootChat = Guid.NewGuid();
            var midChat = Guid.NewGuid();
            var extraChat = Guid.NewGuid();

            var root = BuildProduct(DefaultChatsMode.Custom, (rootChat, "root"));
            var mid = BuildProduct(DefaultChatsMode.Custom, (midChat, "mid"));
            var leaf = BuildProduct(DefaultChatsMode.FromParent);
            root.AddSubProduct(mid);
            mid.AddSubProduct(leaf);

            var sensor = BuildIntegerSensorUnder(leaf);
            AddPolicy(sensor, PolicyDestinationMode.FromParent, extraChat);

            var policy = sensor.Policies.Single(p => p.Destination.IsFromParentChats);
            var handler = policy.TargetChats;

            Assert.Equal(2, handler.Chats.Count);
            Assert.Equal("mid", handler.Chats[midChat]);
            Assert.Equal(extraChat.ToString(), handler.Chats[extraChat]);
            Assert.False(handler.Chats.ContainsKey(rootChat));
        }

        // An ancestor in Empty mode (alerts without notifications) also breaks the chain:
        // IsFromParent is false for any mode other than FromParent.
        [Fact]
        public void EmptyModeMiddleNode_BreaksChain()
        {
            var rootChat = Guid.NewGuid();
            var midChat = Guid.NewGuid();

            var root = BuildProduct(DefaultChatsMode.Custom, (rootChat, "root"));
            var mid = BuildProduct(DefaultChatsMode.Empty, (midChat, "mid"));
            root.AddSubProduct(mid);

            var chats = new IntegerPolicy().GetParentChats(mid);

            Assert.Equal(new Dictionary<Guid, string> { [midChat] = "mid" }, chats);
        }

        [Fact]
        public void NullParent_ReturnsEmpty()
        {
            Assert.Empty(new IntegerPolicy().GetParentChats(null));
        }

        private static ProductModel BuildProduct(DefaultChatsMode mode, params (Guid Id, string Name)[] chats)
        {
            var entity = new ProductEntity
            {
                Id = Guid.NewGuid().ToString(),
                DisplayName = $"p-{Guid.NewGuid():N}",
                DefaultChatsSettings = new PolicyDestinationSettingsEntity
                {
                    Mode = (byte)mode,
                    Chats = chats.ToDictionary(c => c.Id.ToString(), c => c.Name),
                },
            };

            return new ProductModel(entity);
        }

        private static IntegerSensorModel BuildIntegerSensorUnder(ProductModel parent)
        {
            var entity = EntitiesFactory.BuildSensorEntity(type: (byte)SensorType.Integer);
            var sensor = new IntegerSensorModel(entity, null, new Mock<IAlertScheduleProvider>().Object);
            parent.AddSensor(sensor);
            return sensor;
        }

        private static void AddPolicy(IntegerSensorModel sensor, PolicyDestinationMode mode, params Guid[] chats)
        {
            var collection = (SensorPolicyCollection<IntegerValue, IntegerPolicy>)sensor.Policies;

            var update = new PolicyUpdate
            {
                Id = Guid.NewGuid(),
                Template = "tmpl",
                Status = SensorStatus.Ok,
                Destination = new PolicyDestinationUpdate(chats.ToDictionary(c => c, c => c.ToString()), mode),
            };

            collection.TryUpdate(new List<PolicyUpdate> { update }, InitiatorInfo.AsUser("test"), out _);
        }
    }
}
