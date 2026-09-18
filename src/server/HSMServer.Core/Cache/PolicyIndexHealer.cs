using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Cache
{
    // The boot path's self-heal arm over the policy-id index (#1407): the
    // index is a single key mutated by an (now locked) read-modify-write, and
    // rows created before the lock could lose their index entry to a
    // concurrent template apply — the row survives under its own key and the
    // sensor entity still references it, but GetAllPolicies (index-driven)
    // never returns it and the load silently skipped the reference, so a
    // template alert lived in memory until the next restart and vanished
    // without a journal trace. This healer walks the ENTITIES' references and
    // pulls rows the index missed directly by id — existing orphaned rows
    // recover on the first boot after the fix, no repair tooling needed.
    //
    // SENSOR entities only, deliberately: PRODUCT entities also carry legacy
    // regular-policy references, but those are dead at runtime and pruned by
    // the startup migration (CleanupProductOwnedPolicies — see the
    // alerts/feature.md invariant) — healing them would fight the pruning.
    internal static class PolicyIndexHealer
    {
        // Mutates `policies` in place: every sensor-referenced id that is
        // missing from the dictionary but resolves to a row is added. Returns
        // the healed count and the ids that reference NOTHING (true orphans —
        // row gone, reference dangling; the caller logs them, the load skips
        // them as before, but no longer invisibly).
        public static (int Healed, List<Guid> Unresolved) Heal(
            List<SensorEntity> sensorEntities,
            Dictionary<string, PolicyEntity> policies,
            Func<Guid, PolicyEntity> fetchRow)
        {
            var healed = 0;
            List<Guid> unresolved = null;

            foreach (var entity in sensorEntities ?? [])
            {
                foreach (var idText in entity.Policies ?? [])
                {
                    // The reference format is the same Guid.ToString() the
                    // dictionary keys use; an unparseable reference is
                    // pre-existing corruption, not this heal's concern.
                    if (!Guid.TryParse(idText, out var id))
                        continue;

                    var key = id.ToString();

                    if (policies.ContainsKey(key))
                        continue;

                    var row = fetchRow(id);

                    if (row is not null)
                    {
                        policies.Add(key, row);
                        healed++;
                    }
                    else
                        (unresolved ??= []).Add(id);
                }
            }

            return (healed, unresolved);
        }
    }
}
