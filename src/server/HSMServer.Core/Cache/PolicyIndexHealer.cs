using System;
using System.Collections.Generic;
using System.Linq;
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
        // the healed count, the DISTINCT ids that reference NOTHING (true
        // orphans — row gone, reference dangling; the caller logs them, the
        // load skips them as before, but no longer invisibly), and the ids
        // whose row could not be READ (fetchRow threw): a read failure is
        // not an orphan verdict — the caller reports it separately and the
        // next boot retries.
        public static (int Healed, List<Guid> Unresolved, List<Guid> Unread) Heal(
            List<SensorEntity> sensorEntities,
            Dictionary<string, PolicyEntity> policies,
            Func<Guid, PolicyEntity> fetchRow)
        {
            var healed = 0;
            HashSet<Guid> unresolved = null;
            HashSet<Guid> unread = null;

            foreach (var entity in sensorEntities ?? [])
            {
                if (entity is null)
                    continue;

                foreach (var idText in entity.Policies ?? [])
                {
                    // Unparseable reference: pre-existing corruption, not this
                    // heal's concern — left exactly as the load would see it.
                    if (!Guid.TryParse(idText, out var id))
                        continue;

                    // The guard checks BOTH keys. The dictionary's keys are
                    // the normalized Guid.ToString() the index load builds;
                    // the consumer (ApplyPolicies) looks the entry up by the
                    // RAW reference. A non-canonical reference whose row is
                    // indexed under the normalized form is not "missing" —
                    // it gets the raw-key alias for the consumer's lookup
                    // (no fetch, no healed count) instead of being re-healed
                    // on every boot.
                    if (policies.TryGetValue(idText, out var known) || policies.TryGetValue(id.ToString(), out known))
                    {
                        if (!policies.ContainsKey(idText))
                            policies[idText] = known;

                        continue;
                    }

                    // Known-dead from an earlier reference: no repeat fetch,
                    // one unresolved entry — many sensors can share one dead
                    // id, and the boot Warn counts DISTINCT ids.
                    if (unresolved?.Contains(id) ?? false)
                        continue;

                    PolicyEntity row;

                    try
                    {
                        row = fetchRow(id);
                    }
                    catch (Exception)
                    {
                        (unread ??= []).Add(id);
                        continue;
                    }

                    if (row is not null)
                    {
                        // Inserted under the RAW reference — the exact string
                        // the consumer looks up; a normalized key would
                        // "heal" into a lookup the consumer still misses.
                        policies.Add(idText, row);
                        healed++;
                    }
                    else
                        (unresolved ??= []).Add(id);
                }
            }

            return (healed, unresolved?.ToList() ?? [], unread?.ToList() ?? []);
        }
    }
}
