using HSMServer.Model.Notifications;
using HSMServer.Notifications.Chats;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    public class ChatSensorUsageCalculatorTests
    {
        [Fact]
        public void TwoPoliciesSameChat_DedupesToOne()
        {
            var chatX = Guid.NewGuid();

            var set = ChatSensorUsageCalculator.GetEffectiveChats(
                policyChatSets: new[] { new[] { chatX }, new[] { chatX } },
                folderDefaultChats: null);

            Assert.Single(set);
            Assert.Contains(chatX, set);
        }

        [Fact]
        public void RegularAndTtlPolicy_BothUnioned_DedupedPerChat()
        {
            var chatX = Guid.NewGuid();
            var chatY = Guid.NewGuid();

            var set = ChatSensorUsageCalculator.GetEffectiveChats(
                policyChatSets: new[] { new[] { chatX }, new[] { chatX, chatY } },
                folderDefaultChats: null);

            Assert.Equal(2, set.Count);
            Assert.Contains(chatX, set);
            Assert.Contains(chatY, set);
        }

        [Fact]
        public void FolderDefaultChats_UnionedWithPolicyChats()
        {
            var chatX = Guid.NewGuid();
            var chatY = Guid.NewGuid();

            var set = ChatSensorUsageCalculator.GetEffectiveChats(
                policyChatSets: new[] { new[] { chatX } },
                folderDefaultChats: new[] { chatY });

            Assert.Equal(2, set.Count);
            Assert.Contains(chatX, set);
            Assert.Contains(chatY, set);
        }

        [Fact]
        public void FolderDefaultChats_OverlapWithPolicyChats_Deduped()
        {
            var chatX = Guid.NewGuid();

            var set = ChatSensorUsageCalculator.GetEffectiveChats(
                policyChatSets: new[] { new[] { chatX } },
                folderDefaultChats: new[] { chatX });

            Assert.Single(set);
        }

        [Fact]
        public void NoPolicyChats_OnlyFolderDefaultChats_Counted()
        {
            var chatY = Guid.NewGuid();

            var set = ChatSensorUsageCalculator.GetEffectiveChats(
                policyChatSets: Enumerable.Empty<IEnumerable<Guid>>(),
                folderDefaultChats: new[] { chatY });

            Assert.Single(set);
            Assert.Contains(chatY, set);
        }

        [Fact]
        public void NullInputs_YieldEmptySet()
        {
            var set = ChatSensorUsageCalculator.GetEffectiveChats(null, null);
            Assert.Empty(set);
        }

        [Fact]
        public void NullAndEmptyPolicySets_SkippedSafely()
        {
            var chatX = Guid.NewGuid();

            var set = ChatSensorUsageCalculator.GetEffectiveChats(
                policyChatSets: new IEnumerable<Guid>[] { null, Enumerable.Empty<Guid>(), new[] { chatX } },
                folderDefaultChats: null);

            Assert.Single(set);
            Assert.Contains(chatX, set);
        }

        [Theory]
        [InlineData(0, "0 sensors")]
        [InlineData(1, "1 sensor")]
        [InlineData(2, "2 sensors")]
        [InlineData(5, "5 sensors")]
        public void SensorUsageBadgeText_SingularPlural(int count, string expected)
        {
            var vm = new ChatViewModel { SensorUsageCount = count };
            Assert.Equal(expected, vm.SensorUsageBadgeText);
        }
    }
}
