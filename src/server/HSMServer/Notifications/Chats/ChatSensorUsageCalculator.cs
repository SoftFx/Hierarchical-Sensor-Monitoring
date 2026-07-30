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
        internal static HashSet<Guid> GetEffectiveChats(IEnumerable<IEnumerable<Guid>> policyChatSets, IEnumerable<Guid> folderDefaultChats)
        {
            var set = new HashSet<Guid>();

            if (policyChatSets is not null)
                foreach (var chats in policyChatSets)
                    if (chats is not null)
                        set.UnionWith(chats);

            if (folderDefaultChats is not null)
                set.UnionWith(folderDefaultChats);

            return set;
        }

        private HashSet<Guid> ResolveSensorChats(BaseSensorModel sensor)
        {
            IEnumerable<Guid> folderDefaultChats = null;
            // `Root` casts to ProductModel internally; `as` converts an orphaned-sensor cast into a
            // null so the sensor still gets policy-chat credit instead of being dropped wholesale.
            var rootFolderId = (sensor?.Root as ProductModel)?.FolderId;
            if (rootFolderId.HasValue && _folders.TryGetValue(rootFolderId.Value, out FolderModel folder))
                folderDefaultChats = folder.DefaultChats.SelectedChats;

            return GetEffectiveChats(EnumeratePolicyChats(sensor), folderDefaultChats);
        }

        // Yields each policy's effective chat id set (regular + TTL). TargetChats already resolves
        // FromParent against the ProductModel parent chain; folder default chats are added separately
        // — that mirrors TreeValuesCache.SendAlertMessage, which injects folder.DefaultChats at
        // delivery time (Policy.cs has a TODO to fold folder chats into GetParentChats, so the two
        // sets stay distinct today). Disabled policies are intentionally counted — the badge shows
        // where the chat is wired into alert config, not whether it would deliver today.
        private static IEnumerable<IEnumerable<Guid>> EnumeratePolicyChats(BaseSensorModel sensor)
        {
            var policies = sensor?.Policies;
            if (policies is null)
                yield break;

            // TTLPolicies is never null (PolicyCollectionBase initializes to []), but the getter
            // takes a lock — a concurrent reassignment could throw during enumeration; that surfaces
            // inside the try in Compute() and is handled as a best-effort skip.
            foreach (Policy policy in policies.Concat(policies.TTLPolicies.Cast<Policy>()))
            {
                var chats = policy?.TargetChats?.Chats;
                if (chats is not null)
                    yield return chats.Keys;
            }
        }
    }
}
