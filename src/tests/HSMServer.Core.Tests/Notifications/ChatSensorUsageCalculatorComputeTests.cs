using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Folders;
using HSMServer.Model.Folders;
using HSMServer.Notifications.Chats;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    // Integration coverage for the Compute() entry point that sits outside the pure
    // GetEffectiveChats seam: an empty sensor cache yields an empty counts dictionary, proving
    // the singleton's primary public contract. Per-sensor counting rules (dedup, disabled-policy
    // inclusion, folder-default gating, orphan guard) are covered by the static-seam unit tests
    // in ChatSensorUsageCalculatorTests.cs.
    //
    // The per-sensor try/catch (cache mutates concurrently with reads) is exercised in production
    // by the live ITreeValuesCache; BaseSensorModel is sealed-by-convention with abstract members
    // that Moq cannot proxy, so we rely on code review + the seam tests rather than a synthetic
    // throw-from-getter case here.
    public class ChatSensorUsageCalculatorComputeTests
    {
        [Fact]
        public void Compute_NoSensors_ReturnsEmpty()
        {
            var calc = new ChatSensorUsageCalculator(
                CacheReturning(Enumerable.Empty<BaseSensorModel>()),
                FolderManagerStub());

            var counts = calc.Compute();

            Assert.Empty(counts);
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
