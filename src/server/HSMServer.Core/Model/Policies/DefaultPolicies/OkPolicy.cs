using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Core.Model.Policies
{
    internal class OkPolicy : DefaultPolicyBase
    {
        internal OkPolicy(Guid id, BaseNodeModel node) 
        {
            Apply(new PolicyEntity
            {
                Id = id.ToByteArray(),
                Template = TTLPolicy.DefaultTemplate,
                Icon = SensorStatus.Ok.ToIcon(),
            }, node as BaseSensorModel);
        }
    }
}