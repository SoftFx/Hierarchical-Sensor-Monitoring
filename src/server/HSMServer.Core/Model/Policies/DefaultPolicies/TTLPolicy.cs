using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.NodeSettings;
using System;
using System.Text;

namespace HSMServer.Core.Model.Policies
{
    public sealed class TTLPolicy : DefaultPolicyBase
    {
        public const string DefaultIcon = "🕑";
        public const string DefaultTemplate = "[$product]$path";

        private readonly SettingProperty<TimeIntervalModel> _ttl;
        private readonly OkPolicy _okPolicy;


        internal PolicyResult Ok
        {
            get
            {
                _okPolicy.RebuildState();

                return _okPolicy.PolicyResult;
            }
        }


        internal TTLPolicy(BaseNodeModel node, PolicyEntity entity)
        {
            _ttl = node.Settings.TTL;

            Apply(entity ?? new PolicyEntity
            {
                Id = Id.ToByteArray(),
                Template = DefaultTemplate,
                Icon = DefaultIcon,
                Destination = new PolicyDestinationEntity() { AllChats = true },
            }, node as BaseSensorModel);

            _okPolicy = new OkPolicy(this, node);
        }


        internal void FullUpdate(PolicyUpdate update, BaseSensorModel sensor = null)
        {
            Update(update, sensor);

            _okPolicy.Update(update with { Template = _okPolicy.OkTemplate }, sensor);
        }

        internal bool HasTimeout(DateTime? time) => !_ttl.IsEmpty && time.HasValue && _ttl.Value.TimeIsUp(time.Value);

        public override string ToString()
        {
            var sb = new StringBuilder($"If Inactivity period = {_ttl.CurValue}");

            return ActionsToString(sb).ToString();
        }
    }
}