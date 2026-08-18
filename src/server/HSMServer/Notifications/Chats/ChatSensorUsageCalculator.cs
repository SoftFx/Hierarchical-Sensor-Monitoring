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
            // time.
            //
            // Parent-chain rule: this walk stops at the first ancestor in any mode other than
            // FromParent (Custom/Empty/NotInitialized — the picker offers all of them as
            // explicit choices). Same rule as the destination picker UI
            // (DefaultChatViewModel.GetParentChats) and alert delivery (Policy.GetParentChats
            // in HSMServer.Core — aligned in #1330). The badge and delivery agree on every
            // chain shape.
            var inheritedChatsByProduct = new Dictionary<Guid, HashSet<Guid>>();
            var folderChatsByFolderId = new Dictionary<Guid, IEnumerable<Guid>>();

            Exception firstError = null;
            Guid? firstSkippedSensorId = null;

            foreach (var sensor in _cache.GetSensors())
            {
                try
                {
                    var effective = ResolveSensorChats(sensor, inheritedChatsByProduct, folderChatsByFolderId);
                    foreach (var chatId in effective)
                        counts[chatId] = counts.GetValueOrDefault(chatId) + 1;
                }
                catch (InvalidOperationException ex)
                {
                    // Expected race: PolicyDestination.Update clears and refills the plain
                    // Dictionary<Guid, string> without a lock, so an enumerator over .Keys or
                    // .Values thrown mid-mutation. Skip the sensor and let the UI surface "≥N".
                    // Only the first skip carries the full exception — the rest roll up into the
                    // aggregate line after the loop, so a systematic failure on a large install
                    // does not flood the log with one Warn per sensor.
                    firstError ??= ex;
                    firstSkippedSensorId ??= sensor?.Id;
                    skipped++;
                }
            }

            if (skipped > 0)
            {
                var id = firstSkippedSensorId is Guid g ? g.ToString() : "<unknown>";
                _log.Warn(firstError, $"Sensor usage count: skipped {skipped} sensor(s) due to concurrent cache mutation. First skipped sensor: {id}.");
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

        private HashSet<Guid> ResolveSensorChats(
            BaseSensorModel sensor,
            Dictionary<Guid, HashSet<Guid>> inheritedChatsByProduct,
            Dictionary<Guid, IEnumerable<Guid>> folderChatsByFolderId)
        {
            var (policyChatSets, hasAlertCapablePolicy) = EnumeratePolicyChats(sensor, inheritedChatsByProduct);

            IEnumerable<Guid> folderDefaultChats = null;
            // `sensor.Root` casts to ProductModel unconditionally inside the getter
            // (BaseNodeModel.Root). Orphans (Parent == null) have no folder to resolve anyway, so
            // gate the branch on Parent — Root only throws when Parent is null.
            if (hasAlertCapablePolicy && sensor?.Parent is not null)
            {
                var rootFolderId = sensor.Root.FolderId;
                if (rootFolderId.HasValue)
                {
                    var folderId = rootFolderId.Value;
                    if (folderChatsByFolderId.TryGetValue(folderId, out var cached))
                    {
                        folderDefaultChats = cached;
                    }
                    else if (_folders.TryGetValue(folderId, out FolderModel folder))
                    {
                        // Snapshot once per Compute() pass — DefaultChatViewModel mutates
                        // SelectedChats in place (Clear + Add), and a concurrent folder save could
                        // throw mid-union or produce a torn read. Memoized because every sensor
                        // under the same root folder resolves to the same set.
                        var snapshot = folder.DefaultChats.SelectedChats.ToArray();
                        folderChatsByFolderId[folderId] = snapshot;
                        folderDefaultChats = snapshot;
                    }
                }
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
        // modes (no per-policy HashSet allocation — GetEffectiveChats only UnionWith's the
        // contents), and the memoized ancestor chain for FromParent — bypassing Policy.TargetChats,
        // which allocates a fresh Dictionary + handler per call. Disabled policies are
        // intentionally counted — the badge shows where the chat is wired into alert config, not
        // whether it would deliver today.
        private (List<IEnumerable<Guid>> ChatSets, bool HasAlertCapablePolicy) EnumeratePolicyChats(
            BaseSensorModel sensor,
            Dictionary<Guid, HashSet<Guid>> inheritedChatsByProduct)
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

                // Mirror Policy.TargetChats: when Destination.IsFromParentChats, the parent chain
                // is unioned in FIRST, then Destination.Chats is layered on top (TryAdd semantics
                // — explicit chats do not overwrite inherited ones). FromParent + extra chats is
                // a first-class state: the alert form JS keeps the chats array when switching to
                // FromParent (_AlertsFormCollection.cshtml), alert import preserves chats only in
                // FromParent mode (AlertExportViewModel), and PolicyDestination.ToString has a
                // dedicated "from parent chats, {extra}" case. The previous form returned only
                // the inherited set and dropped Destination.Chats, silently undercounting.
                //
                // This resolver's parent-chain walk stops at the first non-inheriting ancestor,
                // matching the picker UI — delivery (Policy.GetParentChats) uses the same rule
                // since #1330.
                IEnumerable<Guid> resolvedChats;
                int resolvedChatCount;
                bool resolvedIsAllChats;

                if (destination.IsFromParentChats && parent is not null)
                {
                    var inherited = ResolveInheritedChats(parent, inheritedChatsByProduct);

                    if (destination.Chats.Count == 0)
                    {
                        // Common case: FromParent with no explicit extras — reuse the memoized set
                        // directly. UnionWith in GetEffectiveChats enumerates without mutating it.
                        resolvedChats = inherited;
                        resolvedChatCount = inherited.Count;
                    }
                    else
                    {
                        var union = new HashSet<Guid>(inherited);
                        foreach (var id in destination.Chats.Keys)
                            union.Add(id);

                        resolvedChats = union;
                        resolvedChatCount = union.Count;
                    }

                    // Destination.IsAllChats is mutually exclusive with IsFromParentChats
                    // (PolicyDestinationMode.AllChats != .FromParent), so this branch is always
                    // false — but pass it through for parity with the Custom/Empty/All branch.
                    resolvedIsAllChats = false;
                }
                else
                {
                    // Read Dictionary.Keys directly — no per-policy HashSet allocation.
                    resolvedChats = destination.Chats.Keys;
                    resolvedChatCount = destination.Chats.Count;
                    resolvedIsAllChats = destination.IsAllChats && (parent?.Settings?.DefaultChats?.Value?.IsAllChats ?? false);
                }

                if (IsAlertCapable(policy, resolvedChatCount, resolvedIsAllChats))
                    hasAlertCapablePolicy = true;

                // chatSets.Add is unconditional — a policy with no Template, or a disabled policy,
                // still contributes its chats. The badge is a configuration-reference count ("is
                // this chat wired into alert config at all"), not a would-it-deliver-today count,
                // so a half-saved policy (Template removed but Destination intact) still counts.
                // This is asymmetric with the folder-default chats union (which IS gated on
                // hasAlertCapablePolicy) — that gating exists specifically to avoid swamping fresh
                // sensors with folder-default chats, not to express admissibility.
                chatSets.Add(resolvedChats);
            }

            foreach (Policy policy in policies)
                Add(policy);

            // TTLPolicies getter takes a lock and returns a snapshot list; concurrent reassignment
            // could throw during enumeration — surfaces inside Compute()'s try/catch as a skip.
            foreach (var ttl in policies.TTLPolicies)
                Add(ttl);

            return (chatSets, hasAlertCapablePolicy);
        }

        // Resolves the inherited chat set for a product, memoized per Compute() pass. Single
        // linear walk up the chain, stopping at the first ancestor in any mode other than
        // FromParent (Custom/Empty/NotInitialized). Matches the destination picker UI
        // (DefaultChatViewModel.GetParentChats) and delivery (Policy.GetParentChats, #1330).
        private HashSet<Guid> ResolveInheritedChats(
            ProductModel parent,
            Dictionary<Guid, HashSet<Guid>> memo)
        {
            if (memo.TryGetValue(parent.Id, out var cached))
                return cached;

            var chats = new HashSet<Guid>();

            for (var node = parent; node is not null; node = node.Parent)
            {
                var curValue = node.Settings.DefaultChats.CurValue;

                foreach (var id in curValue.Chats.Keys)
                    chats.Add(id);

                if (!curValue.IsFromParent)
                    break;
            }

            memo[parent.Id] = chats;
            return chats;
        }
    }
}
