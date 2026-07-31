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
                folderDefaultChats: null,
                includeFolderChats: false);

            Assert.Single(set);
            Assert.Contains(chatX, set);
        }

        [Fact]
        public void TwoPolicySets_BothUnioned_DedupedPerChat()
        {
            var chatX = Guid.NewGuid();
            var chatY = Guid.NewGuid();

            var set = ChatSensorUsageCalculator.GetEffectiveChats(
                policyChatSets: new[] { new[] { chatX }, new[] { chatX, chatY } },
                folderDefaultChats: null,
                includeFolderChats: false);

            Assert.Equal(2, set.Count);
            Assert.Contains(chatX, set);
            Assert.Contains(chatY, set);
        }

        [Fact]
        public void FolderDefaultChats_UnionedWithPolicyChats_WhenIncluded()
        {
            var chatX = Guid.NewGuid();
            var chatY = Guid.NewGuid();

            var set = ChatSensorUsageCalculator.GetEffectiveChats(
                policyChatSets: new[] { new[] { chatX } },
                folderDefaultChats: new[] { chatY },
                includeFolderChats: true);

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
                folderDefaultChats: new[] { chatX },
                includeFolderChats: true);

            Assert.Single(set);
        }

        // Pins the rule that folder-default chats are folded in ONLY when the sensor has at least
        // one alert-capable policy — mirroring TreeValuesCache.SendAlertMessage, which injects
        // folder.DefaultChats per alert rather than unconditionally per sensor. With
        // includeFolderChats=false the folder chat is dropped even though it was passed in.
        [Fact]
        public void FolderDefaultChats_NotIncluded_DroppedEvenWhenPassed()
        {
            var chatY = Guid.NewGuid();

            var set = ChatSensorUsageCalculator.GetEffectiveChats(
                policyChatSets: Enumerable.Empty<IEnumerable<Guid>>(),
                folderDefaultChats: new[] { chatY },
                includeFolderChats: false);

            Assert.Empty(set);
        }

        [Fact]
        public void NullInputs_YieldEmptySet()
        {
            var set = ChatSensorUsageCalculator.GetEffectiveChats(null, null, includeFolderChats: false);
            Assert.Empty(set);
        }

        [Fact]
        public void NullAndEmptyPolicySets_SkippedSafely()
        {
            var chatX = Guid.NewGuid();

            var set = ChatSensorUsageCalculator.GetEffectiveChats(
                policyChatSets: new IEnumerable<Guid>[] { null, Enumerable.Empty<Guid>(), new[] { chatX } },
                folderDefaultChats: null,
                includeFolderChats: false);

            Assert.Single(set);
            Assert.Contains(chatX, set);
        }

        // Pins the user's decision that disabled policies still count: the seam receives each
        // policy's chat set unconditionally (no IsDisabled filter), so a chat wired through a
        // disabled policy contributes. EnumeratePolicyChats is the consumer contract for this.
        [Fact]
        public void PolicyChatSets_AreCounted_Unconditionally()
        {
            var chatX = Guid.NewGuid();
            var chatY = Guid.NewGuid();

            var set = ChatSensorUsageCalculator.GetEffectiveChats(
                policyChatSets: new[] { new[] { chatX }, new[] { chatY } },
                folderDefaultChats: null,
                includeFolderChats: false);

            Assert.Equal(2, set.Count);
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

        // Pins the incomplete-count path: when Compute() skips at least one sensor (concurrent
        // cache mutation), the badge prepends "≥" so the admin cannot mistake a partial count for
        // an authoritative total. Singular/plural rule still applies on top of the prefix.
        [Theory]
        [InlineData(0, "≥0 sensors")]
        [InlineData(1, "≥1 sensor")]
        [InlineData(5, "≥5 sensors")]
        [InlineData(1247, "≥1,247 sensors")]
        public void SensorUsageBadgeText_Incomplete_PrependsGreaterThanOrEqual(int count, string expected)
        {
            var vm = new ChatViewModel { SensorUsageCount = count, SensorUsageIncomplete = true };
            Assert.Equal(expected, vm.SensorUsageBadgeText);
        }

        [Fact]
        public void SensorUsageBadgeTitle_Incomplete_ExplainsPartialCount()
        {
            var vm = new ChatViewModel { SensorUsageIncomplete = true };

            Assert.Contains("incomplete", vm.SensorUsageBadgeTitle);
        }

        [Fact]
        public void SensorUsageBadgeTitle_Complete_NoIncompleteCaveat()
        {
            var vm = new ChatViewModel { SensorUsageIncomplete = false };

            Assert.DoesNotContain("incomplete", vm.SensorUsageBadgeTitle);
        }
    }
}
