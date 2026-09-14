using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace HSMServer.Authentication
{
    // Per-request memoization of the owner-sight decision per DISTINCT product
    // (#1386, extracted from the schedules list): sensors cluster into a handful
    // of products, and the evaluator re-resolves caller + token on every call —
    // one list request must not pay that per item. An admin owner passes every
    // per-product check, so no separate "everywhere" short-circuit exists: the
    // mirror covers it.
    public static class ApiTokenSightExtensions
    {
        public static Func<Guid, bool> MemoizedProductVisibility(this IApiTokenAuthorizationService authorization,
            ClaimsPrincipal principal)
        {
            var visibilityByProduct = new Dictionary<Guid, bool>();

            return productId =>
                visibilityByProduct.TryGetValue(productId, out var visible)
                    ? visible
                    : visibilityByProduct[productId] = authorization.IsVisible(principal,
                        ApiTokenResource.Product(productId));
        }
    }
}
