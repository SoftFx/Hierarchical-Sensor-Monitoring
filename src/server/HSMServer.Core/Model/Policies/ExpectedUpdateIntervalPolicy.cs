namespace HSMServer.Core.Model
{
    public sealed class ExpectedUpdateIntervalPolicy : Policy
    {
        public long ExpectedUpdateInterval { get; set; }


        public ExpectedUpdateIntervalPolicy(long expectedUpdateInterval) : base()
        {
            ExpectedUpdateInterval = expectedUpdateInterval;
        }
    }
}
