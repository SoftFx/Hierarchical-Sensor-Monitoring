using System;

namespace HSMServer.Core.Model.Policies
{
    public abstract class DefaultPolicyBase : Policy
    {
        internal new PolicyResult PolicyResult => new(_sensor.Id, this); //policy always should generate new Result


        protected override AlertState GetState(BaseValue value) => AlertState.BuildBase(value, _sensor);

        protected override PolicyCondition GetCondition(PolicyProperty _) => throw new NotImplementedException();
    }
}