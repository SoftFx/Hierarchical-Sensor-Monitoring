using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Folders;
using HSMServer.Model.Folders;
using HSMServer.Notifications.Chats;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using EntitiesFactory = HSMServer.Core.Tests.Infrastructure.EntitiesFactory;

namespace HSMServer.Core.Tests.Notifications
{
    // Integration coverage for Compute()'s entry-point contract and for the GetParentChats
    // routing change (PR #1327 follow-up). The GetParentChats test pins the 3-level chain with a
    // non-inheriting middle node — the case where the old "walk every ancestor" form disagreed
    // with the UI resolver and with the new stop-on-non-inheriting form.
    public class ChatSensorUsageCalculatorComputeTests
    {
        [Fact]
        public void Compute_NoSensors_ReturnsEmpty()
        {
            var calc = new ChatSensorUsageCalculator(
                CacheReturning(Enumerable.Empty<BaseSensorModel>()),
                FolderManagerStub());

            var (counts, skipped) = calc.Compute();

            Assert.Empty(counts);
            Assert.Equal(0, skipped);
        }

        // Pins the GetParentChats routing change: with a chain root(C) → mid(Custom, not FromParent)
        // → leaf(FromParent), calling GetParentChats(leaf) must STOP at mid and NOT include root's
        // chats. The old code walked every ancestor unconditionally once parent.IsFromParent was
        // true, which disagreed with DefaultChatViewModel.GetParentChats (UI resolver).
        //
        // The memoization path in ChatSensorUsageCalculator.ResolveInheritedChats mirrors this
        // stop-on-non-inheriting semantics, so pinning it here guards both code paths at once.
        [Fact]
        public void GetParentChats_StopsAtFirstNonInheritingAncestor()
        {
            var rootChat = Guid.NewGuid();
            var midChat = Guid.NewGuid();

            var root = BuildProduct(DefaultChatsMode.Custom, (rootChat, "root"));
            var mid = BuildProduct(DefaultChatsMode.Custom, (midChat, "mid"));  // NOT FromParent
            var leaf = BuildProduct(DefaultChatsMode.FromParent);

            root.AddSubProduct(mid);
            mid.AddSubProduct(leaf);

            // GetParentChats is internal on Policy; reach it through BooleanPolicy (no Sensor
            // needed — GetParentChats takes parent explicitly and does not touch policy state).
            var policy = new BooleanPolicy();

            var resolved = policy.GetParentChats(leaf);

            Assert.Contains(midChat, resolved.Keys);
            Assert.DoesNotContain(rootChat, resolved.Keys);
        }

        // Pins that a FromParent policy with ADDITIONAL Destination.Chats still counts those
        // explicit chats. FromParent + extra chats is a first-class state (the alert form JS
        // keeps the chats array when switching to FromParent, alert import preserves chats only
        // in FromParent mode, and PolicyDestination.ToString has a dedicated "from parent chats,
        // {extra}" case). Policy.TargetChats unions Destination.Chats on top of the inherited
        // parent-chain set; the calculator used to return only the inherited set in the FromParent
        // branch and silently drop the extras — an undercount in exactly the failure direction
        // that matters for a blast-radius badge.
        [Fact]
        public void Compute_FromParentPolicyWithExtraChats_CountsBothInheritedAndExplicit()
        {
            var parentChat = Guid.NewGuid();
            var extraChat = Guid.NewGuid();

            var root = BuildProduct(DefaultChatsMode.Custom, (parentChat, "parent"));
            var provider = new Mock<IAlertScheduleProvider>().Object;
            var sensor = BuildIntegerSensorUnder(root, provider);
            AddPolicy(sensor, PolicyDestinationMode.FromParent, extraChat);

            var calc = new ChatSensorUsageCalculator(
                CacheReturning(new[] { sensor }),
                FolderManagerStub());

            var (counts, skipped) = calc.Compute();

            Assert.Equal(0, skipped);
            Assert.Equal(1, counts[parentChat]);
            Assert.Equal(1, counts[extraChat]);
        }

        // Pins the contract that when EVERY ancestor in the chain is FromParent, GetParentChats
        // walks the whole way to the root. This is the "no non-inheriting break" branch and
        // guards against over-eager break-on-first-iteration regressions.
        [Fact]
        public void GetParentChats_AllAncestorsFromParent_WalksToRoot()
        {
            var rootChat = Guid.NewGuid();
            var midChat = Guid.NewGuid();
            var leafChat = Guid.NewGuid();

            var root = BuildProduct(DefaultChatsMode.Custom, (rootChat, "root"));
            var mid = BuildProduct(DefaultChatsMode.FromParent, (midChat, "mid"));
            var leaf = BuildProduct(DefaultChatsMode.FromParent, (leafChat, "leaf"));

            root.AddSubProduct(mid);
            mid.AddSubProduct(leaf);

            var policy = new BooleanPolicy();

            var resolved = policy.GetParentChats(leaf);

            Assert.Equal(new[] { leafChat, midChat, rootChat }.ToHashSet(), resolved.Keys.ToHashSet());
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

        private static IntegerSensorModel BuildIntegerSensorUnder(ProductModel parent, IAlertScheduleProvider provider)
        {
            var entity = EntitiesFactory.BuildSensorEntity(type: (byte)SensorType.Integer);
            var sensor = new IntegerSensorModel(entity, null, provider);
            parent.AddSensor(sensor);
            return sensor;
        }

        private static void AddPolicy(IntegerSensorModel sensor, PolicyDestinationMode mode, params Guid[] chats)
        {
            var collection = (SensorPolicyCollection<IntegerValue, IntegerPolicy>)sensor.Policies;

            var chatDict = chats.ToDictionary(c => c, c => c.ToString());
            var update = new PolicyUpdate
            {
                Id = Guid.NewGuid(),
                Template = "tmpl",
                Status = SensorStatus.Ok,
                Destination = new PolicyDestinationUpdate(chatDict, mode),
            };

            collection.TryUpdate(new List<PolicyUpdate> { update }, InitiatorInfo.AsUser("test"), out _);
        }


        private static ITreeValuesCache CacheReturning(IEnumerable<BaseSensorModel> sensors)
        {
            var mock = new Mock<ITreeValuesCache>();
            mock.Setup(c => c.GetSensors()).Returns(sensors.ToList());
            return mock.Object;
        }

        private static IFolderManager FolderManagerStub()
        {
            var mock = new Mock<IFolderManager>();
            FolderModel _;
            mock.Setup(m => m.TryGetValue(It.IsAny<Guid>(), out _)).Returns(false);
            return mock.Object;
        }
    }
}
