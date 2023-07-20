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

        public override string BuildStateAndComment(BaseValue value, BaseSensorModel sensor, PolicyCondition _) =>
            SetStateAndGetComment(AlertState.BuildBase(value, sensor));
    }
}