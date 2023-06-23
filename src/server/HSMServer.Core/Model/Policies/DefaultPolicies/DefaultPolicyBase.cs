using System;

namespace HSMServer.Core.Model.Policies
{
    public abstract class DefaultPolicyBase : Policy
    {
        internal PolicyResult PolicyResult { get; }


        protected DefaultPolicyBase(Guid sensorId) : base()
        {
            PolicyResult = new PolicyResult(sensorId, this);
        }
    }
}
