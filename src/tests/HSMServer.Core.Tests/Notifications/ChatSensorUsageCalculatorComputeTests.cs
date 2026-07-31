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
    // Integration coverage for Compute()'s entry-point contract: empty cache → empty counts,
    // the calculator's parent-chain resolution (stops at the first non-inheriting ancestor,
    // matching the destination picker UI — diverges from delivery routing, tracked in #1330),
    // FromParent + explicit Destination.Chats union, and the concurrent-mutation skip path
    // (InvalidOperationException → skipped++, UI renders "≥N sensors").
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

        // Pins the calculator's parent-chain resolution: with a chain root(C) → mid(Custom, not
        // FromParent) → leaf(FromParent), a sensor under leaf with a FromParent policy must count
        // mid's chats but NOT root's. ResolveInheritedChats stops at the first ancestor whose
        // DefaultChats.IsFromParent is false. This is the calculator's own badge-counting rule —
        // it does NOT depend on Policy.GetParentChats (which delivers alerts) and is intentionally
        // stricter: a non-inheriting middle node breaks the inheritance for the badge the same way
        // the destination picker UI shows.
        [Fact]
        public void Compute_FromParentUnderNonInheritingMiddle_StopsAtMiddle()
        {
            var rootChat = Guid.NewGuid();
            var midChat = Guid.NewGuid();

            var root = BuildProduct(DefaultChatsMode.Custom, (rootChat, "root"));
            var mid = BuildProduct(DefaultChatsMode.Custom, (midChat, "mid"));  // NOT FromParent
            var leaf = BuildProduct(DefaultChatsMode.FromParent);

            root.AddSubProduct(mid);
            mid.AddSubProduct(leaf);

            var provider = new Mock<IAlertScheduleProvider>().Object;
            var sensor = BuildIntegerSensorUnder(leaf, provider);
            AddPolicy(sensor, PolicyDestinationMode.FromParent);

            var calc = new ChatSensorUsageCalculator(
                CacheReturning(new[] { sensor }),
                FolderManagerStub());

            var (counts, skipped) = calc.Compute();

            Assert.Equal(0, skipped);
            Assert.Equal(1, counts[midChat]);
            Assert.False(counts.ContainsKey(rootChat));
        }

        // Pins that a FromParent policy with ADDITIONAL Destination.Chats still counts those
        // explicit chats. FromParent + extra chats is a first-class state (the alert form JS
        // keeps the chats array when switching to FromParent, alert import preserves chats only
        // in FromParent mode, and PolicyDestination.ToString has a dedicated "from parent chats,
        // {extra}" case). The calculator's FromParent branch layers Destination.Chats on top of
        // the inherited parent-chain set; it used to return only the inherited set and silently
        // drop the extras — an undercount in exactly the failure direction that matters for a
        // blast-radius badge.
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

        // Pins that when EVERY ancestor in the chain is FromParent, the calculator walks all the
        // way to the root. This is the "no non-inheriting break" branch and guards against
        // over-eager break-on-first-iteration regressions in ResolveInheritedChats.
        [Fact]
        public void Compute_AllAncestorsFromParent_WalksToRoot()
        {
            var rootChat = Guid.NewGuid();
            var midChat = Guid.NewGuid();
            var leafChat = Guid.NewGuid();

            var root = BuildProduct(DefaultChatsMode.Custom, (rootChat, "root"));
            var mid = BuildProduct(DefaultChatsMode.FromParent, (midChat, "mid"));
            var leaf = BuildProduct(DefaultChatsMode.FromParent, (leafChat, "leaf"));

            root.AddSubProduct(mid);
            mid.AddSubProduct(leaf);

            var provider = new Mock<IAlertScheduleProvider>().Object;
            var sensor = BuildIntegerSensorUnder(leaf, provider);
            AddPolicy(sensor, PolicyDestinationMode.FromParent);

            var calc = new ChatSensorUsageCalculator(
                CacheReturning(new[] { sensor }),
                FolderManagerStub());

            var (counts, skipped) = calc.Compute();

            Assert.Equal(0, skipped);
            Assert.Equal(1, counts[rootChat]);
            Assert.Equal(1, counts[midChat]);
            Assert.Equal(1, counts[leafChat]);
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
