using HSMServer.Core.Extensions;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using System.Numerics;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests.ModelTests
{
    public class TimeIntervalModelTests
    {
        [Theory]
        [InlineData(TimeInterval.FromParent)]
        public void TimeIsUpTests(TimeInterval interva)
        {

            var model = new TimeIntervalModel();

        }

    }
}
