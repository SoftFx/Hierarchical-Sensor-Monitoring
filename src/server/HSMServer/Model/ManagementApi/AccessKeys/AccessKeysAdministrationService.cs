using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Validation;

namespace HSMServer.Model.ManagementApi.AccessKeys
{
    /// <summary>
    /// The access-key write engine (#1789): issue, modify and remove a
    /// product's agent credentials — the token-side twin of the web UI's
    /// access-keys page. Transport-agnostic: the REST controller and the MCP
    /// write tools render its <see cref="PolicyWriteResult{T}"/> through the
    /// shared envelopes.
    /// </summary>
    // DESIGN: the write semantics stay in the core — this service decides
    // admission (the evaluator's write at the product's ROOT boundary: the
    // owner's ProductManager role + a read-write token, the same gate the
    // web page's ProductRoleFilter enforces), validates with the web form's
    // rules and error strings verbatim (required/length-100/unique-in-product
    // on the display name; at-least-one-permission), maps the wire's
    // permission names to the stored flags, and delegates to the core's
    // AddAccessKey/UpdateAccessKey/RemoveAccessKey — the SAME entries the web
    // UI drives (they answer null on failure; the honest-verification arm of
    // the deletion pattern maps that to a fixed-message 409, never a ghost
    // success). A caller with several problems learns ALL fields in one 400
    // (the schedules' errorMap shape).
    //
    // The key attaches to the ROUTE product (the node the caller named — the
    // read surface lists that node's collection, so a created key is
    // immediately visible through the same route), while admission is keyed
    // on the node's ROOT (#1680). The item scope is the ROUTE product: an
    // unknown key id and a key of another product answer the same 404 (the
    // read surface's anti-enumeration rule), and master keys are unreachable
    // (they live in no product's collection).
    //
    // One deliberate addition over the web flow: every write is journaled as
    // an informational TreeCrud record naming the token owner as initiator
    // (the core journals product creation the same way — credential
    // lifecycle belongs in the audit trail; no undo group, like the
    // product-create record).
    //
    // Stateless over singletons -> Scoped registration.
    public sealed class AccessKeysAdministrationService
    {
        // The web view model's error strings (EditAccessKeyViewModel /
        // AccessKeyPermissionsValidation), verbatim — the area's
        // validation-parity invariant.
        private const string NameRequiredMessage = "Display name is required.";
        private const string NameLengthMessage = "Display name length should be less than 100.";
        private const string NameDuplicateMessage = "Access key with the same name already exists.";
        private const string PermissionsRequiredMessage = "At least one permission should be selected.";

        // The agent-installation default (#1789): enough for an HSM agent to
        // build its tree and send data, without the read-back permission.
        internal const KeyPermissions AgentProfile =
            KeyPermissions.CanSendSensorData | KeyPermissions.CanAddNodes | KeyPermissions.CanAddSensors;

        private const int NameMaxLength = 100;

        // The sibling surfaces' fixed-conflict pattern: the core's null
        // return carries its reason in the server log only.
        private const string NotCreatedMessage = "The access key could not be created.";
        private const string NotUpdatedMessage = "The access key could not be updated.";
        private const string NotRemovedMessage = "The access key could not be removed.";

        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly ITreeValuesCache _cache;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly IUserManager _users;
        private readonly IJournalService _journal;

        public AccessKeysAdministrationService(ITreeValuesCache cache,
            IApiTokenAuthorizationService authorization, IUserManager users, IJournalService journal)
        {
            _cache = cache;
            _authorization = authorization;
            _users = users;
            _journal = journal;
        }


        /// <summary>
        /// Issue a key on the product: admission (the owner's ProductManager
        /// role + a read-write token), the web form's display-name rules, the
        /// permission names (the agent profile when omitted), then the core's
        /// own add path. The result echoes the created key — the GUID in the
        /// echo IS the credential, exactly like the read surface exposes it.
        /// </summary>
        public PolicyWriteResult<AccessKeyDto> CreateAccessKey(Guid productId,
            CreateAccessKeyRequestDto request, ClaimsPrincipal user)
        {
            var resolution = ResolveWritableProduct(productId, user);

            if (resolution.Failure is not null)
                return PolicyWriteResult<AccessKeyDto>.Fail(resolution.Failure.Outcome,
                    resolution.Failure.Errors, resolution.Failure.Message);

            var errorMap = new Dictionary<string, List<string>>();
            var displayName = request?.DisplayName?.Trim();

            ValidateName(resolution.Product, displayName, existingKeyId: null, errorMap);

            var flags = AgentProfile;

            if (request?.Permissions is { } permissionNames)
            {
                if (TryValidatePermissions(permissionNames, out var parsed, out var permissionError))
                    flags = parsed;
                else
                    errorMap.Add("permissions", [permissionError]);
            }

            if (errorMap.Count > 0)
                return PolicyWriteResult<AccessKeyDto>.Fail(PolicyWriteOutcome.Validation, Flatten(errorMap));

            // ExpirationTime is init-only and the (authorId, productId)
            // ctor leaves it at DateTime.MinValue — an API-issued key would
            // be born EXPIRED (IsValidState refuses it on first use, and the
            // expired-unblock guard can never resurrect it). The web form
            // always sets it; "Unlimited" (the default-key value) is this
            // surface's preset too — expiration management is a follow-up.
            var key = new AccessKeyModel(AuthorIdOf(user), resolution.Product.Id)
            {
                ExpirationTime = DateTime.MaxValue,
            }.Update(new AccessKeyUpdate { DisplayName = displayName, Permissions = flags });

            var created = _cache.AddAccessKey(key);

            if (created is null)
                return PolicyWriteResult<AccessKeyDto>.Fail(PolicyWriteOutcome.Conflict, message: NotCreatedMessage);

            Journal("Create access key", resolution.Product, user,
                newValue: $"{displayName} ({created.Id})");

            return PolicyWriteResult<AccessKeyDto>.Ok(AccessKeyDtoMapper.ToDto(created));
        }

        /// <summary>
        /// Modify one key of the product: rename, re-permission, block or
        /// unblock (the kill-switch). Every field is optional — null means
        /// leave unchanged. Unblocking a key whose expiration has passed
        /// lands on 'expired', the web page's own guard.
        /// </summary>
        public PolicyWriteResult<AccessKeyDto> UpdateAccessKey(Guid productId, Guid keyId,
            UpdateAccessKeyRequestDto request, ClaimsPrincipal user)
        {
            var resolution = ResolveWritableProduct(productId, user);

            if (resolution.Failure is not null)
                return PolicyWriteResult<AccessKeyDto>.Fail(resolution.Failure.Outcome,
                    resolution.Failure.Errors, resolution.Failure.Message);

            if (!resolution.Product.AccessKeys.TryGetValue(keyId, out var key) || key is null)
                return PolicyWriteResult<AccessKeyDto>.Fail(PolicyWriteOutcome.NotFound);

            var errorMap = new Dictionary<string, List<string>>();
            var displayName = request?.DisplayName?.Trim();

            if (displayName is not null)
                ValidateName(resolution.Product, displayName, existingKeyId: keyId, errorMap);

            KeyPermissions? flags = null;

            if (request?.Permissions is { } permissionNames)
            {
                if (TryValidatePermissions(permissionNames, out var parsed, out var permissionError))
                    flags = parsed;
                else
                    errorMap.Add("permissions", [permissionError]);
            }

            KeyState? state = null;

            if (request?.State is { } stateName)
            {
                if (stateName == "blocked")
                {
                    state = KeyState.Blocked;
                }
                else if (stateName == "active")
                {
                    // The web page's guard: unblocking an expired key cannot
                    // resurrect it as 'active' — the expiration still stands.
                    state = key.IsExpired ? KeyState.Expired : KeyState.Active;
                }
                else
                {
                    errorMap.Add("state", [$"The state must be 'active' or 'blocked' (got '{stateName}')."]);
                }
            }

            if (errorMap.Count > 0)
                return PolicyWriteResult<AccessKeyDto>.Fail(PolicyWriteOutcome.Validation, Flatten(errorMap));

            var updated = _cache.UpdateAccessKey(new AccessKeyUpdate
            {
                Id = keyId,
                DisplayName = displayName,
                Permissions = flags,
                State = state,
            });

            if (updated is null)
                return PolicyWriteResult<AccessKeyDto>.Fail(PolicyWriteOutcome.Conflict, message: NotUpdatedMessage);

            Journal("Update access key", resolution.Product, user,
                oldValue: key.DisplayName, newValue: updated.DisplayName);

            return PolicyWriteResult<AccessKeyDto>.Ok(AccessKeyDtoMapper.ToDto(updated));
        }

        /// <summary>Remove one key of the product — the credential stops being accepted on its next presentation.</summary>
        public PolicyWriteResult DeleteAccessKey(Guid productId, Guid keyId, ClaimsPrincipal user)
        {
            var resolution = ResolveWritableProduct(productId, user);

            if (resolution.Failure is not null)
                return PolicyWriteResult.Fail(resolution.Failure.Outcome,
                    resolution.Failure.Errors, resolution.Failure.Message);

            if (!resolution.Product.AccessKeys.TryGetValue(keyId, out var key) || key is null)
                return PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);

            var removed = _cache.RemoveAccessKey(keyId);

            if (removed is null)
            {
                // The honest-verification arm of the deletion pattern: a key
                // that vanished concurrently is the plain 404, anything else
                // is the fixed-message conflict.
                return resolution.Product.AccessKeys.ContainsKey(keyId)
                    ? PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict, message: NotRemovedMessage)
                    : PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);
            }

            Journal("Remove access key", resolution.Product, user,
                oldValue: $"{key.DisplayName} ({keyId})");

            return PolicyWriteResult.Ok();
        }


        // The write admission of the read surface's ResolveProduct: unknown
        // and invisible products answer the same 404 BEFORE the evaluator is
        // consulted; a visible-but-not-writable product (Viewer owner or a
        // read-only token) answers the area's uniform 403. Sight and write
        // are both keyed on the node's ROOT (#1680); the KEY itself attaches
        // to the ROUTE product (the read surface lists that collection).
        private (ProductModel Product, PolicyWriteFailure Failure) ResolveWritableProduct(
            Guid productId, ClaimsPrincipal user)
        {
            if (!_cache.TryGetProduct(productId, out var product) || product is null)
                return (null, new PolicyWriteFailure { Outcome = PolicyWriteOutcome.NotFound });

            return _authorization.AuthorizeWrite(user, ApiTokenResource.Product(product.Root.Id)) switch
            {
                ApiTokenAuthorization.Allowed => (product, null),
                ApiTokenAuthorization.Forbidden => (null, new PolicyWriteFailure
                {
                    Outcome = PolicyWriteOutcome.Forbidden,
                    Message = PolicyWriteResult.WriteDeniedMessage,
                }),
                _ => (null, new PolicyWriteFailure { Outcome = PolicyWriteOutcome.NotFound }),
            };
        }

        // The web form's display-name rules in field order (required, length,
        // #1705 single-line, unique-in-product excluding self) — the web
        // strings verbatim, appended to the shared error map.
        private static void ValidateName(ProductModel product, string displayName, Guid? existingKeyId,
            Dictionary<string, List<string>> errorMap)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                errorMap.Add("displayName", [NameRequiredMessage]);

                return;
            }

            if (displayName.Length > NameMaxLength)
            {
                errorMap.Add("displayName", [NameLengthMessage]);

                return;
            }

            if (TextFieldValidator.HasForbiddenCharacter(displayName, out var nameChar, allowLineBreaks: false))
            {
                // #1705: no legitimate name carries control characters or
                // bidi overrides; they spoof every renderer.
                errorMap.Add("displayName",
                    [$"The display name must not contain control or bidirectional-override characters (found {nameChar})."]);

                return;
            }

            if (product.AccessKeys.Values.Any(k => k.Id != existingKeyId && k.DisplayName == displayName))
                errorMap.Add("displayName", [NameDuplicateMessage]);
        }

        // Permission names: at-least-one (the web rule) and every name must
        // be one of the four known ones (nothing is silently dropped). One
        // message per call — the field is one list.
        private static bool TryValidatePermissions(string[] names, out KeyPermissions permissions,
            out string error)
        {
            permissions = 0;

            if (!AccessKeyDtoMapper.TryParsePermissions(names, out permissions, out var unknownName))
            {
                error = $"The permission '{unknownName}' is not one of 'canSendSensorData', 'canAddNodes', 'canAddSensors', 'canReadSensorData'.";

                return false;
            }

            if (permissions == 0)
            {
                error = PermissionsRequiredMessage;

                return false;
            }

            error = null;

            return true;
        }

        private static IDictionary<string, string[]> Flatten(Dictionary<string, List<string>> errorMap) =>
            errorMap.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());

        // The author of the created key is the token's owner. Guid.Empty is
        // the unreachable defensive arm (the evaluator resolved a live owner
        // before any write method runs — the same arm the product-create
        // service carries); the claim always parses on the real paths.
        private static Guid AuthorIdOf(ClaimsPrincipal user) =>
            Guid.TryParse(user?.FindFirst(HsmApiTokenClaims.OwnerUserId)?.Value, out var ownerId)
                ? ownerId
                : Guid.Empty;

        // The informational audit trail of a credential-lifecycle write: a
        // TreeCrud record WITHOUT an undo group (nothing to compensate — the
        // product-create record's shape), naming the token owner as the
        // initiator.
        private void Journal(string enviroment, ProductModel product, ClaimsPrincipal user,
            string oldValue = null, string newValue = null)
        {
            var ownerClaim = user?.FindFirst(HsmApiTokenClaims.OwnerUserId)?.Value;
            var owner = Guid.TryParse(ownerClaim, out var ownerId) ? _users[ownerId] : null;
            var rootId = product.Root.Id;

            try
            {
                _journal.AddRecord(new JournalRecordModel(rootId,
                        InitiatorInfo.AsUser(owner?.Name ?? "api-token"))
                {
                    Enviroment = enviroment,
                    PropertyName = "accessKey",
                    OldValue = oldValue,
                    NewValue = newValue,
                    Kind = JournalKinds.TreeCrud,
                    ProductId = rootId.ToString(),
                });
            }
            catch (Exception ex)
            {
                // The audit trail is best-effort: a journal failure must not
                // fail a write that already landed.
                _logger.Warn(ex, $"Cannot journal the access-key write '{enviroment}' on product '{rootId}'.");
            }
        }
    }
}
