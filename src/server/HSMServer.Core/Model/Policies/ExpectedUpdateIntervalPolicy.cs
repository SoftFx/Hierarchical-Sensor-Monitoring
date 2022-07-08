namespace HSMServer.Core.Model
{
    public sealed class ExpectedUpdateIntervalPolicy : Policy
    {
        public long ExpectedUpdateInterval { get; init; }


        public ExpectedUpdateIntervalPolicy(long expectedUpdateInterval) : base()
        {
            ExpectedUpdateInterval = expectedUpdateInterval;
        }
    }
}
