using System;

namespace HSMServer.Core.Model.Policies
{
    public abstract class DefaultPolicyBase : Policy
    {
        internal PolicyResult PolicyResult { get; }


        protected DefaultPolicyBase(Guid nodeId) : base()
        {
            PolicyResult = new PolicyResult(nodeId, this);
        }


        protected override PolicyCondition GetCondition() => throw new NotImplementedException();
    }
}
