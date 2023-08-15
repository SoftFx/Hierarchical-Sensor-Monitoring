using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Core.Model.Policies
{
    internal sealed class OkPolicy : DefaultPolicyBase
    {
        internal OkPolicy(Guid id, BaseNodeModel node) 
        {
            Apply(new PolicyEntity
            {
                Id = id.ToByteArray(),
                Template = $"$status {TTLPolicy.DefaultTemplate}",
            }, node as BaseSensorModel);
        }
    }
}