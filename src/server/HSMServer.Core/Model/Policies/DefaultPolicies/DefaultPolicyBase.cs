using System;

namespace HSMServer.Core.Model.Policies
{
    public abstract class DefaultPolicyBase : Policy
    {
        protected override AlertState GetState(BaseValue value) => AlertState.BuildBase(value, _sensor);

        protected override PolicyCondition GetCondition() => throw new NotImplementedException();
    }
}