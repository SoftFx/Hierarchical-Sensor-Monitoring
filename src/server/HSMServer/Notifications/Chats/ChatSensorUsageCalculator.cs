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

        public Dictionary<Guid, int> Compute()
        {
            var counts = new Dictionary<Guid, int>();

            foreach (var sensor in _cache.GetSensors())
            {
                try
                {
                    var effective = ResolveSensorChats(sensor);
                    foreach (var chatId in effective)
                        counts[chatId] = counts.GetValueOrDefault(chatId) + 1;
                }
                catch (Exception ex)
                {
                    // The cache mutates concurrently with reads (PolicyDestination.Update clears
                    // Chats without a lock); skip the sensor and keep the rest of the count useful.
                    _log.Warn(ex, $"Sensor usage count: skipping sensor {sensor.Id}");
                }
            }

            return counts;
        }

        // Pure union/dedup seam, surfaced for unit testing. Per-sensor resolution (which reads live
        // cache state) stays in the private overload below.
        //
        // folderDefaultChats is folded in only when includeFolderChats is true — mirroring
        // TreeValuesCache.SendAlertMessage, which injects folder.DefaultChats only when at least
        // one alert actually fires for the sensor. A sensor with no alert-capable policy (the
        // common case for a fresh sensor — AddDefault is commented out at TreeValuesCache.cs:2439)
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

        private HashSet<Guid> ResolveSensorChats(BaseSensorModel sensor)
        {
            var (policyChatSets, hasAlertCapablePolicy) = EnumeratePolicyChats(sensor);

            IEnumerable<Guid> folderDefaultChats = null;
            // `sensor.Root` casts to ProductModel unconditionally inside the getter
            // (BaseNodeModel.cs:50); the previous `as ProductModel` guard was a no-op for orphan
            // sensors. Orphans (Parent == null) have no folder to resolve anyway, so gate the
            // branch on Parent — Root only throws when Parent is null.
            if (hasAlertCapablePolicy && sensor?.Parent is not null)
            {
                var rootFolderId = sensor.Root.FolderId;
                if (rootFolderId.HasValue && _folders.TryGetValue(rootFolderId.Value, out FolderModel folder))
                    folderDefaultChats = folder.DefaultChats.SelectedChats;
            }

            return GetEffectiveChats(policyChatSets, folderDefaultChats, hasAlertCapablePolicy);
        }

        // Mirrors AlertResult.IsValidAlert (AlertResult.cs:93): a policy is alert-capable when it
        // has a template AND a destination that would resolve to at least one chat — explicit
        // (Custom with chats), AllChats, or FromParent (resolves against the product chain, may
        // be empty in practice but still counts as "would deliver if a chat were configured").
        // Empty/NotInitialized modes never deliver. Folder-default chats are only meaningful as
        // a delivery target when some policy could actually fire.
        private static bool IsAlertCapable(Policy policy) =>
            policy is not null
            && policy.Template is not null
            && (policy.Destination.IsFromParentChats || policy.Destination.IsAllChats || policy.Destination.Chats.Count > 0);

        // Yields each policy's effective chat id set (regular + TTL). TargetChats already resolves
        // FromParent against the ProductModel parent chain; folder default chats are added separately
        // — that mirrors TreeValuesCache.SendAlertMessage, which injects folder.DefaultChats at
        // delivery time. Disabled policies are intentionally counted — the badge shows where the
        // chat is wired into alert config, not whether it would deliver today.
        private static (IEnumerable<IEnumerable<Guid>> ChatSets, bool HasAlertCapablePolicy) EnumeratePolicyChats(BaseSensorModel sensor)
        {
            var policies = sensor?.Policies;
            if (policies is null)
                return (Enumerable.Empty<IEnumerable<Guid>>(), false);

            var chatSets = new List<IEnumerable<Guid>>();
            var hasAlertCapablePolicy = false;

            void Add(Policy policy)
            {
                if (policy is null)
                    return;

                if (IsAlertCapable(policy))
                    hasAlertCapablePolicy = true;

                var keys = policy.TargetChats?.Chats?.Keys;
                if (keys is not null)
                    chatSets.Add(keys);
            }

            foreach (Policy policy in policies)
                Add(policy);

            // TTLPolicies getter takes a lock and returns a snapshot list; concurrent reassignment
            // could throw during enumeration — surfaces inside Compute()'s try/catch as a skip.
            foreach (var ttl in policies.TTLPolicies)
                Add(ttl);

            return (chatSets, hasAlertCapablePolicy);
        }
    }
}
