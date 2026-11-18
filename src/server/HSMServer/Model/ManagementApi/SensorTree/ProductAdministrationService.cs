using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.TableOfChanges;
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
    // DESIGN: creation semantics stay in the core — this service only decides
    // admission (the boundary-less write gate of the evaluator: any live
    // read-write caller, mirroring the web UI where any authenticated user
    // creates a product), validates the name with the SAME rules and error
    // strings the web view model enforces (required, length 100, charset,
    // root-name uniqueness — the core's _productsByName race guard answers
    // the concurrent-duplicate case the pre-check cannot see) and delegates
    // the write itself to TreeValuesCache.AddProductAsync, the SAME entry the
    // web UI drives: the default access key, the author id and the TreeCrud
    // journal record naming the initiator all happen in that one place. The
    // author does NOT get a manager role from creation itself (the core never
    // touches roles) — the web flow relies on the author being granted the
    // role out-of-band, and this surface deliberately keeps that behavior.
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

        // AddProductViewModel's charset, verbatim.
        private const string NamePattern = @"^[0-9a-zA-Z .,_\-=#:;%&*()]*$";

        private const int NameMaxLength = 100;

        // The core's race guard throws on a concurrent duplicate with this
        // phrase in the message; every other InvalidOperationException it
        // throws is a queue/persistence failure (a 409, not a validation
        // error).
        private const string DuplicateNameRaceMarker = "name already in use";

        private static readonly System.Text.RegularExpressions.Regex NameRegex =
            new(NamePattern, System.Text.RegularExpressions.RegexOptions.Compiled);

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
        /// creates the product's default access key). The result echoes the
        /// created product.
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

            try
            {
                var product = await _cache.AddProductAsync(trimmed, AuthorIdOf(user), InitiatorOf(user));

                return PolicyWriteResult<ProductDto>.Ok(SensorTreeDtoMapper.ToProductDto(product));
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
                // landed, the message carries the core's own reason.
                return PolicyWriteResult<ProductDto>.Fail(PolicyWriteOutcome.Conflict, message: ex.Message);
            }
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
        private InitiatorInfo InitiatorOf(ClaimsPrincipal user)
        {
            var ownerClaim = user?.FindFirst(HsmApiTokenClaims.OwnerUserId)?.Value;

            var owner = Guid.TryParse(ownerClaim, out var ownerId) ? _users[ownerId] : null;

            return InitiatorInfo.AsUser(owner?.Name ?? "api-token");
        }
    }
}
