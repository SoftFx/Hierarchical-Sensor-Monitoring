using System;
using System.Collections.Generic;
using System.Linq;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.TableOfChanges;
using NLog;

namespace HSMServer.Core.Model.Policies
{
    /// <summary>
    /// The policy collection of a table sensor (ADR-0021, #1844): table sensors have no alerts, so it holds no data
    /// policy and refuses every update that would add one. Its TTL list stays as the base builds it from the entity
    /// (empty for a table sensor, which never inherits one); a table sensor never times out
    /// (<see cref="TableSensorModel.CheckTimeout"/>).
    /// </summary>
    internal sealed class TableSensorPolicyCollection : SensorPolicyCollection
    {
        internal const string NoAlertsError = "Table sensors have no alerts.";

        private static readonly Logger _logger = LogManager.GetLogger(nameof(TableSensorPolicyCollection));


        internal override void Attach(BaseNodeModel model)
        {
            base.Attach(model);

            NotificationResult = new();
            PolicyResult = new();
        }

        internal override void AddPolicy<U>(U policy) => throw new InvalidOperationException(NoAlertsError);

        internal override void AddDefault(DefaultAlertsOptions options) { }

        internal override void AddLargeFileAlert(InitiatorInfo initiator) { }

        internal override void ApplyPolicies(List<string> policyIds, Dictionary<string, PolicyEntity> allPolicies)
        {
            // A table sensor is never given a policy; one on its entity was written by hand or by a bug.
            // Left in the database, not applied.
            if (policyIds is { Count: > 0 })
                _logger.Warn($"Table sensor {_model?.Id}: {policyIds.Count} stored policies are ignored ({NoAlertsError})");
        }

        internal override bool TryUpdate(List<PolicyUpdate> updates, InitiatorInfo initiator, out string error,
            List<PolicyUpdateFailure> policyFailures)
        {
            if (updates is null || updates.Count == 0)
            {
                error = null;
                return true;
            }

            error = NoAlertsError;
            policyFailures?.AddRange(updates.Select(_ => new PolicyUpdateFailure(PolicyUpdateFailureKind.Rejected, NoAlertsError)));

            return false;
        }

        internal override void RemovePolicy(Guid policyId, InitiatorInfo initiator = null) { }

        public override IEnumerator<Policy> GetEnumerator() => Enumerable.Empty<Policy>().GetEnumerator();
    }
}
