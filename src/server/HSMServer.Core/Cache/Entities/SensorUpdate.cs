using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Cache.Entities
{
    public class SensorUpdate
    {
        public Guid Id { get; init; }

        public string Description { get; init; }

        public ExpectedUpdateIntervalUpdate Interval { get; init; }

        public string Unit { get; init; }
    }


    public class ExpectedUpdateIntervalUpdate
    {
        public TimeInterval ExpectedUpdatePeriod { get; init; }

        public long CustomPeriod { get; init; }


        internal bool IsEmpty => ExpectedUpdatePeriod == TimeInterval.Custom && CustomPeriod == 0;


        internal bool IsEqual(ExpectedUpdateIntervalPolicy policy) =>
            ExpectedUpdatePeriod == policy.ExpectedUpdatePeriod && CustomPeriod == policy.CustomPeriod;
    }
}
