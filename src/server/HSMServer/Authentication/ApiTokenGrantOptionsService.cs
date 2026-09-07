using System;
using System.Collections.Generic;
using System.Linq;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using HSMServer.Folders;
using HSMServer.Model.Authentication;

namespace HSMServer.Authentication
{
    // One picker option: a boundary the owner may anchor grants at, with the catalog
    // operations the owner may currently perform there. Kind is the JSON-friendly text
    // form of ApiTokenBoundaryKind ("global"/"product"/"folder"); Id is empty for the
    // global boundary and the canonical Guid text otherwise.
    public sealed record ApiTokenBoundaryOptions(string Kind, string Id, string Name, IReadOnlyList<string> Operations);

    // Issuance-side owner filter (initiative step 4): the create form may offer only
    // what the owner can do RIGHT NOW, and every grant the server accepts must pass the
    // same filter — the picker's hiding is a UX aid, never the enforcement. The rules
    // deliberately mirror ApiTokenAuthorizationService (OwnerCanSee/OwnerCanPerform):
    // reads need any role at the boundary, writes the Manager role, the global boundary
    // is admin-only, and product rights come from ProductsRoles with NO folder fallback.
    public interface IApiTokenGrantOptionsService
    {
        // Boundaries visible to this owner, each with its grantable operations in
        // catalog order. Stale role entries (product/folder since deleted) are skipped.
        List<ApiTokenBoundaryOptions> GetBoundaryOptions(User owner);

        // Whether the owner may mint THIS operation+boundary pair right now. The
        // controller validates every requested grant through this predicate.
        bool IsGrantableByOwner(User owner, string operation, ApiTokenBoundaryKind kind, string boundaryId);
    }

    public sealed class ApiTokenGrantOptionsService : IApiTokenGrantOptionsService
    {
        private readonly ITreeValuesCache _cache;
        private readonly IFolderManager _folders;


        public ApiTokenGrantOptionsService(ITreeValuesCache cache, IFolderManager folders)
        {
            _cache = cache;
            _folders = folders;
        }


        public List<ApiTokenBoundaryOptions> GetBoundaryOptions(User owner)
        {
            var result = new List<ApiTokenBoundaryOptions>();

            if (owner.IsAdmin)
            {
                result.Add(new(ApiTokenBoundaryKind.Global.ToString().ToLowerInvariant(), string.Empty,
                    "Global", OperationsAt(ApiTokenResourceKind.Global, canWrite: true)));

                foreach (var product in _cache.GetProducts().OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase))
                    result.Add(new(ApiTokenBoundaryKind.Product.ToString().ToLowerInvariant(),
                        product.Id.ToString(), product.DisplayName,
                        OperationsAt(ApiTokenResourceKind.Product, canWrite: true)));

                foreach (var folder in _folders.GetValues().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                    result.Add(new(ApiTokenBoundaryKind.Folder.ToString().ToLowerInvariant(),
                        folder.Id.ToString(), folder.Name,
                        OperationsAt(ApiTokenResourceKind.Folder, canWrite: true)));

                return result;
            }

            // Distinct product ids from the materialised per-product roles: HSM keeps
            // folder roles folded into ProductsRoles, so this is the app's own view of
            // what the user can see — no extra folder fallback here.
            foreach (var productId in owner.ProductsRoles.Select(r => r.Item1).Distinct())
            {
                if (!_cache.TryGetProduct(productId, out var product) || product is null)
                    continue;

                result.Add(new(ApiTokenBoundaryKind.Product.ToString().ToLowerInvariant(),
                    productId.ToString(), product.DisplayName,
                    OperationsAt(ApiTokenResourceKind.Product, canWrite: owner.IsManager(productId))));
            }

            foreach (var folderId in owner.FoldersRoles.Keys)
            {
                if (!_folders.TryGetValue(folderId, out var folder) || folder is null)
                    continue;

                result.Add(new(ApiTokenBoundaryKind.Folder.ToString().ToLowerInvariant(),
                    folderId.ToString(), folder.Name,
                    OperationsAt(ApiTokenResourceKind.Folder, canWrite: owner.IsFolderManager(folderId))));
            }

            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            return result;
        }


        public bool IsGrantableByOwner(User owner, string operation, ApiTokenBoundaryKind kind, string boundaryId)
        {
            if (!ApiTokenOperations.IsValid(operation))
                return false;

            var resourceKind = kind switch
            {
                ApiTokenBoundaryKind.Global => ApiTokenResourceKind.Global,
                ApiTokenBoundaryKind.Product => ApiTokenResourceKind.Product,
                ApiTokenBoundaryKind.Folder => ApiTokenResourceKind.Folder,
                _ => (ApiTokenResourceKind?)null,
            };

            if (resourceKind is null || !ApiTokenOperations.IsValidBoundary(operation, kind))
                return false;

            // Same per-kind conjunction the picker uses, so a hidden pair is also an
            // ungrantable pair: visibility for reads, the Manager role for writes, the
            // global boundary and its operations admin-only, and the boundary must
            // still exist (a stale role entry anchors nothing).
            var canWrite = false;

            switch (kind)
            {
                case ApiTokenBoundaryKind.Global:
                    if (!owner.IsAdmin || !string.IsNullOrEmpty(boundaryId))
                        return false;
                    break;

                case ApiTokenBoundaryKind.Product:
                {
                    if (!Guid.TryParse(boundaryId, out var productId) ||
                        !_cache.TryGetProduct(productId, out var product) || product is null)
                        return false;

                    // IsProductAvailable carries the IsAdmin fallback; plain
                    // IsUserProduct would deny an admin without per-product roles.
                    if (!owner.IsProductAvailable(productId))
                        return false;

                    canWrite = owner.IsManager(productId);
                    break;
                }

                case ApiTokenBoundaryKind.Folder:
                {
                    if (!Guid.TryParse(boundaryId, out var folderId) ||
                        !_folders.TryGetValue(folderId, out var folder) || folder is null)
                        return false;

                    if (!owner.IsFolderAvailable(folderId))
                        return false;

                    canWrite = owner.IsFolderManager(folderId);
                    break;
                }

                default:
                    return false;
            }

            return ApiTokenOperations.IsWrite(operation) ? canWrite : true;
        }


        private static IReadOnlyList<string> OperationsAt(ApiTokenResourceKind kind, bool canWrite)
        {
            var boundaryKind = kind switch
            {
                ApiTokenResourceKind.Global => ApiTokenBoundaryKind.Global,
                ApiTokenResourceKind.Product => ApiTokenBoundaryKind.Product,
                ApiTokenResourceKind.Folder => ApiTokenBoundaryKind.Folder,
                _ => (ApiTokenBoundaryKind?)null,
            };

            if (boundaryKind is null)
                return Array.Empty<string>();

            var operations = new List<string>();

            foreach (var operation in ApiTokenOperations.All)
            {
                if (!ApiTokenOperations.IsValidBoundary(operation, boundaryKind.Value))
                    continue;

                // No boundary visibility check here: the callers already established the
                // owner sees this boundary; only the write gate differs per role.
                if (ApiTokenOperations.IsWrite(operation) && !canWrite)
                    continue;

                operations.Add(operation);
            }

            return operations;
        }
    }
}
