using HSMServer.Core.Cache;
using HSMServer.Core.ChangeSets;
using HSMServer.Core.TableOfChanges;
using HSMServer.Authentication;
using HSMServer.Model.Authentication;
using System;

namespace HSMServer.Model.ManagementApi.ChangeSets
{
    // The cookie-`User` twin of the token evaluator's per-entry checks
    // (#1570 web UI): the evaluator resolves HsmApiToken principals only, so
    // the UI maps its role predicates onto the same boundaries. Admin sees
    // and rolls back everything; product/folder roles gate their own
    // entities; schedule entries (Global) stay admin-only.
    //
    // The entry-level checks lean on the RECORDED parentage (the entry's
    // ParentId, the image's RootProductId) — the write-time boundary, no
    // live lookup. The per-resource check (the restores' seam) resolves a
    // sensor's live root product through the cache: the compensating write
    // authorizes against today's tree, exactly like the evaluator does
    // internally.
    internal sealed class CookieUserAccess : IChangeSetAccess
    {
        private readonly User _user;
        private readonly ITreeValuesCache _cache;

        public CookieUserAccess(User user, ITreeValuesCache cache)
        {
            _user = user;
            _cache = cache;
        }

        public bool CanRead(ChangeSetEntry entry) =>
            _user is null ? false : EntryResourceOf(entry) switch
            {
                { Kind: ApiTokenResourceKind.Global } => _user.IsAdmin,
                { Kind: ApiTokenResourceKind.Folder } resource => _user.IsAdmin || _user.IsFolderManager(resource.Id),
                // The RECORDED root, like the sensor arm: a ProductTtl entry's
                // entity may be a sub-product, but a web user's product roles
                // are granted on ROOT products — resolving the sub-product's
                // own id would hide (or expose, on write) the entry.
                { Kind: ApiTokenResourceKind.Product } resource => _user.IsProductAvailable(entry.ParentId ?? resource.Id),
                { Kind: ApiTokenResourceKind.Sensor } resource => _user.IsProductAvailable(entry.ParentId ?? resource.Id),
                _ => false,
            };

        public bool CanWrite(ChangeSetEntry entry) =>
            _user is null ? false : EntryResourceOf(entry) switch
            {
                { Kind: ApiTokenResourceKind.Global } => _user.IsAdmin,
                { Kind: ApiTokenResourceKind.Folder } resource => _user.IsFolderManager(resource.Id),
                { Kind: ApiTokenResourceKind.Product } resource => _user.IsManager(entry.ParentId ?? resource.Id),
                { Kind: ApiTokenResourceKind.Sensor } resource => _user.IsManager(entry.ParentId ?? resource.Id),
                _ => false,
            };

        public bool CanReadNode(NodePolicyImage image) =>
            _user is not null && _user.IsProductAvailable(image.RootProductId);

        public bool CanWriteNode(NodePolicyImage image) =>
            _user is not null && _user.IsManager(image.RootProductId);

        public bool CanWrite(ApiTokenResource resource) =>
            _user is null ? false : resource.Kind switch
            {
                ApiTokenResourceKind.Global => _user.IsAdmin,
                ApiTokenResourceKind.Folder => _user.IsFolderManager(resource.Id),
                ApiTokenResourceKind.Product => _user.IsManager(resource.Id),
                ApiTokenResourceKind.Sensor => _user.IsManager(RootProductOf(resource.Id)),
                _ => false,
            };

        private Guid RootProductOf(Guid sensorId) =>
            _cache.GetSensor(sensorId)?.Root?.Id ?? sensorId;

        private static (ApiTokenResourceKind Kind, Guid Id)? EntryResourceOf(ChangeSetEntry entry) =>
            entry.Kind switch
            {
                ChangeSetEntryKind.SensorDataPolicies or ChangeSetEntryKind.SensorTtlPolicies =>
                    (ApiTokenResourceKind.Sensor, entry.EntityId),
                ChangeSetEntryKind.ProductTtlPolicies => (ApiTokenResourceKind.Product, entry.EntityId),
                ChangeSetEntryKind.AlertTemplate when entry.ParentId is { } folder =>
                    (ApiTokenResourceKind.Folder, folder),
                ChangeSetEntryKind.AlertSchedule => (ApiTokenResourceKind.Global, Guid.Empty),
                _ => null,
            };
    }
}
