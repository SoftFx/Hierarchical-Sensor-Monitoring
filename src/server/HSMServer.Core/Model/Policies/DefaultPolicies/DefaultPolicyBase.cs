using System;
using HSMCommon.Model;


namespace HSMServer.Core.Model.Policies
{
    public abstract class DefaultPolicyBase : Policy
    {
        internal new PolicyResult PolicyResult => new(this); //policy always should generate new Result


        internal override bool UseScheduleManagerLogic => false;


        protected override AlertState GetState(BaseValue value) => AlertState.BuildBase(value, Sensor);

        protected override PolicyCondition GetCondition(PolicyProperty _) => throw new NotImplementedException();
    }
}