using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.Alerts;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// The product-creation write engine (#1786): the first step of the
    /// agent-installation scenario — the token-side twin of the web UI's
    /// add-product modal. Transport-agnostic: the REST controller and the MCP
    /// tool render its <see cref="PolicyWriteResult{T}"/> through the shared
    /// envelopes every other administration service uses.
    /// </summary>
    // DESIGN: the creation semantics stay in the core — this service only decides
    // admission (the boundary-less write gate of the evaluator: any live
    // read-write caller, mirroring the web UI where any authenticated user
    // creates a product), validates the name with the SAME rules and error
    // strings the web view model enforces (required, length 100, charset,
    // root-name uniqueness — the core's _productsByName race guard answers
    // the concurrent-duplicate case the pre-check cannot see) and delegates
    // the write itself to TreeValuesCache.AddProductAsync, the SAME entry the
    // web UI drives: the default access key, the author id and the TreeCrud
    // journal record naming the initiator all happen in that one place.
    //
    // ONE deliberate divergence from the web flow: the core grants the author
    // NO role on creation, and the web UI assigns roles out-of-band — an
    // unattended creator would end up unable to SEE the product it just made
    // (sight is role-based), stuck with an id that 404s. This surface
    // therefore grants the creator the ProductManager role itself, through
    // the web UI's own role-assignment pattern (EditUserRole's
    // ProductsRoles/UpdateUser round-trip) — what a role-less owner creates
    // is immediately visible and manageable by that owner, which the
    // agent-installation scenario requires (issue #1787's key surface is
    // gated on the product Manager role). ACCEPTED CONSEQUENCE: a resolvable
    // product role is one of the caller-wide gate's arms (CanSeeAnyBoundary),
    // so a role-less account that creates its first product also opens the
    // caller-wide reads (global alert schedules, the chats id discovery) —
    // the web UI already shows those resources to every logged-in user, and
    // the alternative (an author-sight rule outside the role model) would
    // fragment the sight model the whole /api/v1 area is built on; pinned by
    // a test. The grant runs AFTER the journaled creation and is best-effort
    // on a persistence failure: the product exists, so failing the request
    // would read as "not created" (a retry would trip the duplicate-name
    // 400) — the failure is logged and an admin can grant the role by hand.
    //
    // Stateless over singletons -> Scoped registration.
    public sealed class ProductAdministrationService
    {
        // The web view model's error strings (AddProductViewModel), verbatim —
        // the area's validation-parity invariant.
        private const string NameRequiredMessage = "New product name is required.";
        private const string NameLengthMessage = "New product name length should be less than 100.";
        private const string NameDuplicateMessage = "Product with the same name already exists.";
        private const string NameCharsetMessage = "New product name contains forbidden characters.";

        // The sibling surfaces' fixed-conflict pattern (the deletion's
        // NotRemovedMessage): the core's internal reason never reaches the
        // API client — it is logged server-side instead.
        private const string NotCreatedMessage = "The product could not be created.";

        // AddProductViewModel's charset, verbatim.
        private const string NamePattern = @"^[0-9a-zA-Z .,_\-=#:;%&*()]*$";

        private const int NameMaxLength = 100;

        // The core's race guard throws on a concurrent duplicate with this
        // phrase in the message; every other InvalidOperationException it
        // throws is a queue/persistence failure (a 409, not a validation
        // error).
        private const string DuplicateNameRaceMarker = "name already in use";

        private static readonly Regex NameRegex = new(NamePattern, RegexOptions.Compiled);

        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly ITreeValuesCache _cache;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly IUserManager _users;

        public ProductAdministrationService(ITreeValuesCache cache,
            IApiTokenAuthorizationService authorization, IUserManager users)
        {
            _cache = cache;
            _authorization = authorization;
            _users = users;
        }


        /// <summary>
        /// Create a root product: admission first (any live read-write
        /// caller), then the name rules of the web view model, then the core's
        /// own add path (which also writes the TreeCrud journal record and
        /// creates the product's default access key), then the ProductManager
        /// role grant that makes the product visible and manageable by its
        /// creator. The result echoes the created product.
        /// </summary>
        public async Task<PolicyWriteResult<ProductDto>> CreateProductAsync(string name, ClaimsPrincipal user)
        {
            if (_authorization.AuthorizeProductCreate(user) != ApiTokenAuthorization.Allowed)
                return PolicyWriteResult<ProductDto>.Fail(PolicyWriteOutcome.Forbidden,
                    message: PolicyWriteResult.WriteDeniedMessage);

            // The core trims into ProductModel itself; validate the trimmed
            // form so a whitespace-only name is the required-name error, not a
            // product with an empty display name.
            var trimmed = name?.Trim();

            var errors = ValidateName(trimmed);

            if (errors is not null)
                return PolicyWriteResult<ProductDto>.Fail(PolicyWriteOutcome.Validation, errors);

            // The common duplicate case, answered before any state changes —
            // the web view model's UniqueValidation check (exact match, root
            // products, the same _productsByName index).
            if (_cache.GetProductByName(trimmed) is not null)
                return PolicyWriteResult<ProductDto>.Fail(PolicyWriteOutcome.Validation,
                    NameError(NameDuplicateMessage));

            Core.Model.ProductModel product;

            try
            {
                product = await _cache.AddProductAsync(trimmed, AuthorIdOf(user), InitiatorOf(user));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains(DuplicateNameRaceMarker))
            {
                // A concurrent create won the name between the pre-check and
                // the core's own atomic reservation — the same validation
                // error, not a conflict.
                return PolicyWriteResult<ProductDto>.Fail(PolicyWriteOutcome.Validation,
                    NameError(NameDuplicateMessage));
            }
            catch (InvalidOperationException ex)
            {
                // The core's queue/persistence failure — the write never
                // landed; the reason stays in the server log, the client
                // gets the fixed message (the sibling surfaces' pattern).
                _logger.Warn(ex, $"Cannot add root product '{trimmed}': the core rejected the request.");

                return PolicyWriteResult<ProductDto>.Fail(PolicyWriteOutcome.Conflict, message: NotCreatedMessage);
            }

            // The grant runs AFTER the journaled creation and OUTSIDE the
            // create's error mapping: the product exists, so no grant
            // failure may turn the 201 into a "not created" answer (a retry
            // would trip the duplicate-name 400). GrantManagerRoleAsync
            // swallows and logs everything of its own.
            await GrantManagerRoleAsync(product.Id, user);

            return PolicyWriteResult<ProductDto>.Ok(SensorTreeDtoMapper.ToProductDto(product));
        }


        // The web view model's rules in its declaration order (required,
        // length, charset) — one error per name, field-keyed like every other
        // 400 of the area. The uniqueness rule needs cache state and runs
        // separately, after these shape checks.
        private static Dictionary<string, string[]> ValidateName(string trimmed)
        {
            if (string.IsNullOrEmpty(trimmed))
                return NameError(NameRequiredMessage);

            if (trimmed.Length > NameMaxLength)
                return NameError(NameLengthMessage);

            if (!NameRegex.IsMatch(trimmed))
                return NameError(NameCharsetMessage);

            return null;
        }

        private static Dictionary<string, string[]> NameError(string message) =>
            new() { ["name"] = [message] };

        // The author of the created product is the token's owner — the mirror
        // of the web flow's CurrentUser (#1384: the token acts as its owner).
        // The evaluator already resolved a live owner, so the claim parses;
        // Guid.Empty is the unreachable defensive arm.
        private static Guid AuthorIdOf(ClaimsPrincipal user) =>
            Guid.TryParse(user?.FindFirst(HsmApiTokenClaims.OwnerUserId)?.Value, out var ownerId)
                ? ownerId
                : Guid.Empty;

        // The write initiator mirrors every other management write: the
        // token's OWNER as a user, so the audit trail names the owner.
        private InitiatorInfo InitiatorOf(ClaimsPrincipal user) =>
            InitiatorInfo.AsUser(OwnerOf(user)?.Name ?? "api-token");

        // The creator's own Manager role on the new product — the web UI's
        // role-assignment pattern (EditUserRole), see the DESIGN comment.
        //
        // Concurrency: this is a PROGRAMMATIC writer an agent may call in
        // parallel, while every authorization decision of the same owner
        // READS the same ProductsRoles list. Grants for one owner are
        // therefore serialized on the owner instance, and the list is
        // REPLACED by a fresh snapshot (never mutated in place): a reader
        // that fetched the property enumerates a list this code will never
        // touch again, so the evaluator cannot hit "Collection was modified"
        // from under a create. The web UI's own in-place mutations are
        // unchanged (interactive, low-concurrency — the pre-existing
        // pattern); the residual window (two parallel creates by the SAME
        // owner persisting out of order) is the web flow's own hazard and
        // accepted here.
        //
        // The role REPLACES any stale entry the user holds for the same
        // product id (a retried create after a failed removal cannot
        // accumulate roles).
        private async Task GrantManagerRoleAsync(Guid productId, ClaimsPrincipal user)
        {
            var owner = OwnerOf(user);

            if (owner is null)
                return; // unreachable after the gate: the evaluator resolved a live owner

            try
            {
                Task<bool> persist;

                lock (owner)
                {
                    var roles = new List<(Guid, ProductRoleEnum)>(owner.ProductsRoles);

                    roles.RemoveAll(role => role.Item1 == productId);
                    roles.Add((productId, ProductRoleEnum.ProductManager));

                    owner.ProductsRoles = roles;

                    // Started inside the lock so the entity snapshot
                    // (User.ToEntity enumerates ProductsRoles) is serialized
                    // with the swap; only the await happens outside it.
                    persist = _users.UpdateUser(owner);
                }

                if (!await persist)
                    _logger.Warn($"Cannot persist the ProductManager role on product '{productId}' for '{owner.Name}': the user update did not persist — the role stays ACTIVE IN MEMORY until the next restart and then disappears; grant the role by hand.");
            }
            catch (Exception ex)
            {
                // The product exists and is journaled: any grant failure is
                // logged and swallowed — the 201 must stand, and an admin
                // can grant the role by hand.
                _logger.Warn(ex, $"Cannot grant the ProductManager role on product '{productId}' to '{owner.Name}': the grant failed after the product was created; grant the role by hand.");
            }
        }

        private User OwnerOf(ClaimsPrincipal user)
        {
            var ownerClaim = user?.FindFirst(HsmApiTokenClaims.OwnerUserId)?.Value;

            return Guid.TryParse(ownerClaim, out var ownerId) ? _users[ownerId] : null;
        }
    }
}
