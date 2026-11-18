using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Extensions;

namespace HSMServer.Model.ManagementApi.AccessKeys
{
    /// <summary>
    /// The access-key read implementation (#1787): one product's keys,
    /// paginated, and one key by id — the id source for the write surface
    /// (#1789) and the "has the installed agent connected" signal (the key's
    /// lastUseTime/lastIp ride on the DTO; no new endpoint is needed).
    /// </summary>
    // Sight is the PRODUCT's, resolved at its ROOT like every product-scoped
    // read of the area (#1680 — the route id may be any visible node, and a
    // folder role materializes per root): an unknown and an invisible product
    // answer the SAME 404, a sighted one lists every key of the product it
    // names. There is no per-item filtering — the keys of one product are one
    // sight unit (unlike the folder-sighted template lists), so the admission
    // answers the whole request. Master keys never appear: they are not part
    // of any product's collection, and the item read refuses them (and a key
    // belonging to a DIFFERENT product) with the same plain 404 — the key id
    // space is global and must not become an enumeration oracle across
    // products. Transport-agnostic (ManagementReadResult), shared with the
    // MCP list tool — the #1393 pattern.
    public sealed class AccessKeysReadService
    {
        private readonly ITreeValuesCache _cache;
        private readonly IApiTokenAuthorizationService _authorization;

        public AccessKeysReadService(ITreeValuesCache cache, IApiTokenAuthorizationService authorization)
        {
            _cache = cache;
            _authorization = authorization;
        }


        /// <summary>
        /// One product's access keys, ordered by display name then id, paginated.
        /// An unknown and an invisible product answer the same 404.
        /// </summary>
        public ManagementReadResult<ApiPageDto<AccessKeyDto>> ListProductAccessKeys(
            ClaimsPrincipal user, Guid productId, int page, int pageSize)
        {
            var resolution = ResolveProduct(user, productId);

            if (resolution.Failure is not null)
                return ManagementReadResult<ApiPageDto<AccessKeyDto>>.FromFailure(resolution.Failure);

            var keys = resolution.Product.AccessKeys.Values
                .OrderBy(k => k.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(k => k.Id)
                .Select(ToDto)
                .ToList();

            return ManagementReadResult<ApiPageDto<AccessKeyDto>>.Ok(Page(keys, page, pageSize));
        }

        /// <summary>
        /// One key of one product by id. Unknown product, invisible product, a
        /// key of another product and a server-wide master key all answer the
        /// same 404.
        /// </summary>
        public ManagementReadResult<AccessKeyDto> GetProductAccessKey(
            ClaimsPrincipal user, Guid productId, Guid keyId)
        {
            var resolution = ResolveProduct(user, productId);

            if (resolution.Failure is not null)
                return ManagementReadResult<AccessKeyDto>.FromFailure(resolution.Failure);

            // Scoped to the ROUTE's product: a valid key id of a different
            // product (or a master key, which lives in no product's
            // collection) is the same plain 404 as an unknown id — the id
            // space is global and must not become a cross-product oracle.
            if (!resolution.Product.AccessKeys.TryGetValue(keyId, out var key) || key is null)
                return ManagementReadResult<AccessKeyDto>.Fail(ManagementReadOutcome.NotFound);

            return ManagementReadResult<AccessKeyDto>.Ok(ToDto(key));
        }


        // Unknown id: the plain area 404, before the evaluator call — the rule
        // that keeps unknown and invisible indistinguishable. Sight is keyed
        // on the node's ROOT product (#1680: the route id may be a nested
        // folder; a folder role materializes per root).
        private (ProductModel Product, ManagementReadFailure Failure) ResolveProduct(
            ClaimsPrincipal user, Guid productId)
        {
            if (!_cache.TryGetProduct(productId, out var product) || product is null)
                return (null, new ManagementReadFailure { Outcome = ManagementReadOutcome.NotFound });

            var decision = _authorization.AuthorizeRead(user, ApiTokenResource.Product(product.Root.Id));

            return decision == ApiTokenAuthorization.Allowed
                ? (product, null)
                : (null, new ManagementReadFailure
                {
                    // The Forbidden arm is defensive in the owner-mirror read
                    // model (an in-sight target never 403s a read); NotFound is
                    // the indistinguishable answer for everything the owner
                    // cannot see.
                    Outcome = decision == ApiTokenAuthorization.Forbidden
                        ? ManagementReadOutcome.Forbidden
                        : ManagementReadOutcome.NotFound,
                    Message = "The token's owner cannot see this product.",
                });
        }

        private static ApiPageDto<TItem> Page<TItem>(List<TItem> items, int page, int pageSize)
        {
            (page, pageSize) = ApiPagination.Normalize(page, pageSize);

            var totalPages = ApiPagination.TotalPagesOf(items.Count, pageSize);
            page = ApiPagination.ClampPage(page, totalPages);

            return new ApiPageDto<TItem>
            {
                Items = [.. items.Skip((page - 1) * pageSize).Take(pageSize)],
                Page = page,
                PageSize = pageSize,
                TotalCount = items.Count,
                TotalPages = totalPages,
            };
        }

        private static AccessKeyDto ToDto(AccessKeyModel key) => new()
        {
            Id = key.Id,
            DisplayName = key.DisplayName,
            ProductId = key.ProductId,
            State = key.State switch
            {
                KeyState.Active => "active",
                KeyState.Expired => "expired",
                KeyState.Blocked => "blocked",
                // Unreachable while the enum has exactly the three members
                // above — but a future member must not silently read as
                // 'active' (a label admission decisions key on).
                _ => "unknown",
            },
            Permissions = PermissionsOf(key.Permissions),
            CreationTime = key.CreationTime.ToUtcInstant(),
            ExpirationTime = key.ExpirationTime.ToUtcInstant(),
            LastUseTime = key.LastUseTime == DateTime.MinValue ? null : key.LastUseTime.ToUtcInstant(),
            LastIp = key.LastIP,
            AuthorId = key.AuthorId,
        };

        // The stored flags are a long; the wire carries their names — the
        // agent reads permissions, not bits (the enum-table style of the
        // area's DTO remarks).
        private static string[] PermissionsOf(KeyPermissions permissions)
        {
            List<string> names = new(4);

            if ((permissions & KeyPermissions.CanSendSensorData) != 0)
                names.Add("canSendSensorData");
            if ((permissions & KeyPermissions.CanAddNodes) != 0)
                names.Add("canAddNodes");
            if ((permissions & KeyPermissions.CanAddSensors) != 0)
                names.Add("canAddSensors");
            if ((permissions & KeyPermissions.CanReadSensorData) != 0)
                names.Add("canReadSensorData");

            return [.. names];
        }
    }
}
