using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model.Policies
{
    internal sealed class OkPolicy : DefaultPolicyBase
    {
        private readonly TTLPolicy _ttl;

        internal string OkTemplate => $"$status {_ttl.Template}";


        internal OkPolicy(TTLPolicy policy, BaseNodeModel node)
        {
            _ttl = policy;

            Apply(new PolicyEntity
            {
                Id = policy.Id.ToByteArray(),
                Destination = policy.Destination?.ToEntity(),
                Template = OkTemplate,
            }, node as BaseSensorModel);
        }
    }
}