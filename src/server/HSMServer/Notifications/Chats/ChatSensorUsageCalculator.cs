using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Folders;
using HSMServer.Model.Folders;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Notifications.Chats
{
    public sealed class ChatSensorUsageCalculator
    {
        private static readonly Logger _log = LogManager.GetLogger(nameof(ChatSensorUsageCalculator));

        private readonly ITreeValuesCache _cache;
        private readonly IFolderManager _folders;

        public ChatSensorUsageCalculator(ITreeValuesCache cache, IFolderManager folders)
        {
            _cache = cache;
            _folders = folders;
        }

        // Returns the per-chat sensor counts plus the number of sensors that were skipped due to
        // a concurrent cache mutation. The caller surfaces `skipped > 0` as "≥N sensors" so the
        // admin does not mistake a partial count for an authoritative total — which would be
        // actively misleading on a badge whose entire purpose is judging edit/remove blast radius.
        public (Dictionary<Guid, int> counts, int skipped) Compute()
        {
            var counts = new Dictionary<Guid, int>();
            var skipped = 0;

            // Per-Compute memo of inherited chat sets, keyed by product id. A FromParent policy
            // resolves the whole ancestor chain; we walk each ancestor's chain once and share the
            // result across every policy/sensor that hangs off the same parent — instead of the
            // per-policy TargetChats call, which re-walks and allocates a fresh Dictionary each
            // time. Mirrors the (post-routing-fix) semantics of Policy.GetParentChats: walk up
            // while each ancestor's DefaultChats.IsFromParent is true, stop at the first that
            // isn't.
            var inheritedChatsByProduct = new Dictionary<Guid, (HashSet<Guid> Chats, bool IsAllChats)>();

            foreach (var sensor in _cache.GetSensors())
            {
                try
                {
                    var effective = ResolveSensorChats(sensor, inheritedChatsByProduct);
                    foreach (var chatId in effective)
                        counts[chatId] = counts.GetValueOrDefault(chatId) + 1;
                }
                catch (Exception ex)
                {
                    // The cache mutates concurrently with reads (PolicyDestination.Update clears
                    // Chats without a lock). Increment `skipped` so the UI can render "≥N sensors"
                    // and signal the uncertainty — silently logging would understate the count.
                    _log.Warn(ex, $"Sensor usage count: skipping sensor {sensor.Id}");
                    skipped++;
                }
            }

            return (counts, skipped);
        }

        // Pure union/dedup seam, surfaced for unit testing. Per-sensor resolution (which reads live
        // cache state) stays in the private overload below.
        //
        // folderDefaultChats is folded in only when includeFolderChats is true — mirroring
        // TreeValuesCache.SendAlertMessage, which injects folder.DefaultChats only when at least
        // one alert actually fires for the sensor. A sensor with no alert-capable policy (the
        // common case for a fresh sensor — AddDefault is commented out in TreeValuesCache.AddSensor)
        // would otherwise count every folder-default chat, swamping the badge with false positives.
        internal static HashSet<Guid> GetEffectiveChats(
            IEnumerable<IEnumerable<Guid>> policyChatSets,
            IEnumerable<Guid> folderDefaultChats,
            bool includeFolderChats)
        {
            var set = new HashSet<Guid>();

            if (policyChatSets is not null)
                foreach (var chats in policyChatSets)
                    if (chats is not null)
                        set.UnionWith(chats);

            if (includeFolderChats && folderDefaultChats is not null)
                set.UnionWith(folderDefaultChats);

            return set;
        }

        private HashSet<Guid> ResolveSensorChats(BaseSensorModel sensor, Dictionary<Guid, (HashSet<Guid> Chats, bool IsAllChats)> inheritedChatsByProduct)
        {
            var (policyChatSets, hasAlertCapablePolicy) = EnumeratePolicyChats(sensor, inheritedChatsByProduct);

            IEnumerable<Guid> folderDefaultChats = null;
            // `sensor.Root` casts to ProductModel unconditionally inside the getter
            // (BaseNodeModel.Root). Orphans (Parent == null) have no folder to resolve anyway, so
            // gate the branch on Parent — Root only throws when Parent is null.
            if (hasAlertCapablePolicy && sensor?.Parent is not null)
            {
                var rootFolderId = sensor.Root.FolderId;
                if (rootFolderId.HasValue && _folders.TryGetValue(rootFolderId.Value, out FolderModel folder))
                    folderDefaultChats = folder.DefaultChats.SelectedChats;
            }

            return GetEffectiveChats(policyChatSets, folderDefaultChats, hasAlertCapablePolicy);
        }

        // Mirrors AlertResult.IsValidAlert: a policy is alert-capable when it has a template AND a
        // destination that resolves to at least one chat. `resolvedIsAllChats` is the EFFECTIVE
        // flag (parent's DefaultChats.IsAllChats ANDed with Destination.IsAllChats), not the raw
        // Destination.IsAllChats — checking the raw flag would mis-classify a sensor whose only
        // policy is AllChats-mode under a product whose DefaultChats is not All. Empty /
        // NotInitialized modes never resolve to chats.
        private static bool IsAlertCapable(Policy policy, int resolvedChatCount, bool resolvedIsAllChats) =>
            policy is not null
            && policy.Template is not null
            && (policy.Destination.IsFromParentChats || resolvedIsAllChats || resolvedChatCount > 0);

        // Yields each policy's resolved chat id set (regular + TTL). For each policy we resolve
        // the effective destination ONCE: Destination.Chats.Keys directly for Custom/Empty/All
        // modes, and the memoized ancestor chain for FromParent — bypassing Policy.TargetChats,
        // which allocates a fresh Dictionary + handler per call. Disabled policies are
        // intentionally counted — the badge shows where the chat is wired into alert config, not
        // whether it would deliver today.
        private (List<IEnumerable<Guid>> ChatSets, bool HasAlertCapablePolicy) EnumeratePolicyChats(
            BaseSensorModel sensor,
            Dictionary<Guid, (HashSet<Guid> Chats, bool IsAllChats)> inheritedChatsByProduct)
        {
            var policies = sensor?.Policies;
            if (policies is null)
                return (new List<IEnumerable<Guid>>(), false);

            var chatSets = new List<IEnumerable<Guid>>();
            var hasAlertCapablePolicy = false;

            void Add(Policy policy)
            {
                if (policy is null)
                    return;

                var destination = policy.Destination;
                var parent = sensor.Parent;

                IEnumerable<Guid> resolvedChatKeys;
                bool resolvedIsAllChats;

                if (destination.IsFromParentChats && parent is not null)
                {
                    var (chats, parentIsAllChats) = ResolveInheritedChats(parent, inheritedChatsByProduct);
                    resolvedChatKeys = chats;
                    // PolicyDestinationHandler.IsAllChats = parent.DefaultChats.IsAllChats && Destination.IsAllChats.
                    resolvedIsAllChats = parentIsAllChats && destination.IsAllChats;
                }
                else
                {
                    resolvedChatKeys = destination.Chats.Keys;
                    resolvedIsAllChats = destination.IsAllChats && (parent?.Settings?.DefaultChats?.Value?.IsAllChats ?? false);
                }

                if (IsAlertCapable(policy, resolvedChatKeys.Count(), resolvedIsAllChats))
                    hasAlertCapablePolicy = true;

                chatSets.Add(resolvedChatKeys);
            }

            foreach (Policy policy in policies)
                Add(policy);

            // TTLPolicies getter takes a lock and returns a snapshot list; concurrent reassignment
            // could throw during enumeration — surfaces inside Compute()'s try/catch as a skip.
            foreach (var ttl in policies.TTLPolicies)
                Add(ttl);

            return (chatSets, hasAlertCapablePolicy);
        }

        // Resolves the inherited chat set for a product, memoized per Compute() pass. Mirrors the
        // (post-routing-fix) Policy.GetParentChats: single linear walk up the chain, stopping at
        // the first ancestor whose DefaultChats.IsFromParent is false. AllChats is ANDed across
        // the chain — true only if every visited ancestor has it set. This previously reentered
        // itself once per ancestor (O(2^depth) allocations); the single-walk form is what the
        // DefaultChatViewModel UI resolver already does.
        private (HashSet<Guid> Chats, bool IsAllChats) ResolveInheritedChats(
            ProductModel parent,
            Dictionary<Guid, (HashSet<Guid> Chats, bool IsAllChats)> memo)
        {
            if (memo.TryGetValue(parent.Id, out var cached))
                return cached;

            var chats = new HashSet<Guid>();
            var isAllChats = true;

            for (var node = parent; node is not null; node = node.Parent)
            {
                var curValue = node.Settings.DefaultChats.CurValue;

                foreach (var id in curValue.Chats.Keys)
                    chats.Add(id);

                isAllChats = isAllChats && curValue.IsAllChats;

                if (!curValue.IsFromParent)
                    break;
            }

            var entry = (chats, isAllChats);
            memo[parent.Id] = entry;
            return entry;
        }
    }
}
