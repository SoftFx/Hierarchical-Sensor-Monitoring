using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
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
    }
}
