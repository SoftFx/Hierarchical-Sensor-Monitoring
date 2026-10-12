using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMCommon.Model;
using HSMCommon.TaskResult;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Core.TableOfChanges;
using HSMServer.Extensions;
using HSMServer.Folders;
using HSMServer.Notifications.Chats;

namespace HSMServer.Model.ManagementApi.Alerts
{
    /// <summary>
    /// The write engine of the alert-administration surface (#1500): item-level
    /// CRUD over the data and TTL policies of sensors and products. Transport-
    /// agnostic — the REST controllers render its <see cref="PolicyWriteResult{T}"/>
    /// through the uniform error contract; MCP write tools (phase 2) will render
    /// the same failures as tool error text.
    /// </summary>
    // DESIGN (fixed by the #1500 decision table): the service never talks to the
    // policy collections directly. It reads the current list, applies the ONE
    // requested change, and sends a single atomic FULL-LIST SensorUpdate /
    // ProductUpdate — exactly the machinery the web editor drives, with the same
    // last-write-wins semantics versus the web editor and no new core paths.
    // API-to-API writes to one node are SERIALIZED by a per-node gate (see
    // _writeGates, #1501 round-1) — two full-list merges built from the same
    // snapshot would otherwise each acknowledge while the second silently drops
    // the first's policy. A partial list would drop everything not re-asserted
    // (full-list semantics), so every merge re-asserts every surviving policy,
    // TTL intervals included (a null TTL is an explicit reset-to-parent, not
    // "keep").
    //
    // The gates live in THIS class's fields, so the registration must be a
    // SINGLETON (#1501 round-3, F1): a per-request instance would quietly hand
    // concurrent writes to one node different (empty) gate maps. All
    // constructor dependencies are singletons in DI; the gate registry is the
    // only state and is lock-protected.
    //
    // Authorization precedes existence checks and validation, per the area's
    // anti-enumeration rule: the evaluator resolves the 404/403 split itself
    // (unknown and invisible ids answer the SAME 404), and no body-shape error
    // may reveal that a target exists. Semantic validation runs only after the
    // caller is admitted and answers 422.
    public sealed class PolicyAdministrationService
    {
        public const int MaxPoliciesPerNode = 100;
        public const int MaxChatsPerPolicy = 20;

        private const string TemplateOwnedMessage =
            "The policy was created by an alert template; only its disable toggle may change here — edit or remove the template instead.";

        // NotInitialized is accepted on WRITE too, as an echo: reads of policies
        // whose destination was never configured carry it, and a GET body must
        // PATCH back cleanly (the round-trip rule). It never carries chats.
        private static readonly string[] KnownDestinationModes =
        [
            nameof(PolicyDestinationMode.FromParent),
            nameof(PolicyDestinationMode.Empty),
            nameof(PolicyDestinationMode.AllChats),
            nameof(PolicyDestinationMode.Custom),
            nameof(PolicyDestinationMode.NotInitialized),
        ];

        private readonly ITreeValuesCache _cache;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly IUserManager _users;
        private readonly IChatsManager _chats;
        private readonly IFolderManager _folders;
        private readonly IAlertScheduleProvider _schedules;

        // #1501 round-1 (F2): per-node write serialization. Every write reads a
        // node's full policy list, merges ONE item and sends the whole list back
        // (full-list semantics: ids missing from the list are removed) — two
        // concurrent writes to the SAME node would each acknowledge while the
        // second list silently drops the first write's policy. The gate spans
        // resolve -> merge -> send -> the post-write verification read.
        //
        // KEYING: the id of the node whose list is being replaced — the SENSOR
        // id for sensor data policies, sensor TTL policies and product-routed
        // data policies (the OWNING sensor's key, never the product's: the
        // product surface only addresses the aggregate; the write itself goes
        // through the owning sensor's full list), and the PRODUCT id for
        // product TTL policies (ProductUpdate.TTLPolicies replaces the
        // product's own list). Different nodes never share a gate, so writes
        // to distinct nodes still proceed in parallel.
        //
        // #1501 round-2 (F3): the registry is REFERENCE-COUNTED, not a plain
        // map of eternal semaphores. A user of a gate (holding OR waiting)
        // registers under _gateSync before waiting and unregisters after its
        // write; the entry leaves the map only when the LAST registered user
        // leaves. That keeps the map bounded by in-flight writes (an idle
        // gate is removed) while the map can never hand one writer a gate
        // that a simultaneous removal is about to strand: a
        // remove-only-if-idle check (CurrentCount == 1) has a window where
        // an arriving writer takes the doomed gate just before its removal
        // and a third writer then creates a fresh one — two gates for one
        // node, mutual exclusion lost. Users is the honest "holding or
        // waiting" count; the semaphore itself still serializes.
        private readonly object _gateSync = new();
        private readonly Dictionary<Guid, (SemaphoreSlim Gate, int Users)> _writeGates = new();

        public PolicyAdministrationService(ITreeValuesCache cache, IApiTokenAuthorizationService authorization,
            IUserManager users, IChatsManager chats, IFolderManager folders, IAlertScheduleProvider schedules)
        {
            _cache = cache;
            _authorization = authorization;
            _users = users;
            _chats = chats;
            _folders = folders;
            _schedules = schedules;
        }


        // === Data policies on a sensor ===

        /// <summary>
        /// Create one data policy on the sensor; the id is server-generated and
        /// every other policy of the sensor rides through untouched.
        /// </summary>
        public Task<PolicyWriteResult<PolicyDto>> CreateSensorPolicyAsync(Guid sensorId, PolicyDto dto,
            ClaimsPrincipal user) =>
            LockedWriteAsync(ResolveWritableSensor(sensorId, user), () => CreateSensorPolicyCoreAsync(sensorId, dto, user));

        private async Task<PolicyWriteResult<PolicyDto>> CreateSensorPolicyCoreAsync(Guid sensorId, PolicyDto dto,
            ClaimsPrincipal user)
        {
            var (sensor, failure) = ResolveWritableSensor(sensorId, user);

            if (failure is not null)
                return PolicyWriteResult<PolicyDto>.Fail(failure.Outcome, failure.Errors, failure.Message);

            // A JSON-null body reaches here only from MCP (REST's
            // [ApiController] binding rejects it with a 400 before the
            // service runs): answer the field-keyed shape the tool contract
            // promises, never a NullReferenceException.
            if (dto is null)
                return PolicyWriteResult<PolicyDto>.Fail(PolicyWriteOutcome.Invalid,
                    new Dictionary<string, string[]> { ["policy"] = ["The policy body is required."] });

            if (!TryValidateDataDto(dto, sensor.Type, sensorId, AvailableChats(sensor),
                    out var conditions, out var errors))
                return PolicyWriteResult<PolicyDto>.Fail(PolicyWriteOutcome.Invalid, errors);

            if (sensor.Policies.Count() >= MaxPoliciesPerNode)
                return PolicyWriteResult<PolicyDto>.Fail(PolicyWriteOutcome.Invalid, PolicyCountError("policies"));

            var initiator = InitiatorOf(user);
            var policyId = Guid.NewGuid();

            // Full-list merge: every existing policy re-asserted, the new one appended.
            var merged = sensor.Policies.Select(p => new PolicyUpdate(p, initiator)).ToList();
            merged.Add(AlertPolicyDtoMapper.ToUpdate(dto, policyId, initiator, conditions, AvailableChats(sensor)));

            return await SendSensorUpdateAsync(sensor, policyId, merged, initiator);
        }

        /// <summary>
        /// Replace the full content of one data policy (the route id wins; a
        /// template-owned policy accepts only the disable toggle — anything else
        /// answers 409).
        /// </summary>
        public Task<PolicyWriteResult<PolicyDto>> UpdateSensorPolicyAsync(Guid sensorId, Guid policyId,
            PolicyDto dto, ClaimsPrincipal user) =>
            LockedWriteAsync(ResolveWritableSensor(sensorId, user), () => UpdateSensorPolicyCoreAsync(sensorId, policyId, dto, user));

        private async Task<PolicyWriteResult<PolicyDto>> UpdateSensorPolicyCoreAsync(Guid sensorId, Guid policyId,
            PolicyDto dto, ClaimsPrincipal user)
        {
            var (sensor, failure) = ResolveWritableSensor(sensorId, user);

            if (failure is not null)
                return PolicyWriteResult<PolicyDto>.Fail(failure.Outcome, failure.Errors, failure.Message);

            // A JSON-null body (MCP-only; REST binding rejects it first)
            // answers the field-keyed shape, never a NullReferenceException.
            if (dto is null)
                return PolicyWriteResult<PolicyDto>.Fail(PolicyWriteOutcome.Invalid,
                    new Dictionary<string, string[]> { ["policy"] = ["The policy body is required."] });

            var existing = sensor.Policies.FirstOrDefault(p => p.Id == policyId);

            if (existing is null)
                return PolicyWriteResult<PolicyDto>.Fail(PolicyWriteOutcome.NotFound);

            if (existing.TemplateId is not null && !IsPureDataToggle(dto, existing))
                return PolicyWriteResult<PolicyDto>.Fail(PolicyWriteOutcome.Conflict, message: TemplateOwnedMessage);

            // A pure toggle on a template-owned policy skips content validation
            // (#1501 round-1, F3): template-minted content can legitimately sit
            // outside the API's expression range (properties/operations the
            // editor lacks, non-Const targets reading back as target: null,
            // no-effect bodies), and the core applies ONLY the disable toggle
            // for user-initiated updates to template-owned policies — full
            // validation would answer 422 to an echoed GET body whose content
            // is never written anyway.
            List<PolicyConditionUpdate> conditions = null;

            if (existing.TemplateId is null &&
                !TryValidateDataDto(dto, sensor.Type, sensorId, AvailableChats(sensor),
                    out conditions, out var errors))
                return PolicyWriteResult<PolicyDto>.Fail(PolicyWriteOutcome.Invalid, errors);

            var initiator = InitiatorOf(user);

            var merged = sensor.Policies
                .Where(p => p.Id != policyId)
                .Select(p => new PolicyUpdate(p, initiator))
                .ToList();

            // Template-owned toggle: the STORED content rides through unchanged
            // (the copy constructor carries its TemplateId/TemplateAlertId —
            // server-owned linkage, #1501 round-1, F1) with only IsDisabled
            // taken from the body.
            merged.Add(existing.TemplateId is not null
                ? new PolicyUpdate(existing, initiator) { IsDisabled = dto.IsDisabled }
                : AlertPolicyDtoMapper.ToUpdate(dto, policyId, initiator, conditions, AvailableChats(sensor),
                    existing.TemplateId, existing.TemplateAlertId));

            return await SendSensorUpdateAsync(sensor, policyId, merged, initiator);
        }

        /// <summary>Remove one data policy; every other policy of the sensor rides through untouched.</summary>
        public Task<PolicyWriteResult> DeleteSensorPolicyAsync(Guid sensorId, Guid policyId, ClaimsPrincipal user) =>
            LockedWriteAsync(ResolveWritableSensor(sensorId, user), () => DeleteSensorPolicyCoreAsync(sensorId, policyId, user));

        private async Task<PolicyWriteResult> DeleteSensorPolicyCoreAsync(Guid sensorId, Guid policyId,
            ClaimsPrincipal user)
        {
            var (sensor, failure) = ResolveWritableSensor(sensorId, user);

            if (failure is not null)
                return PolicyWriteResult.Fail(failure.Outcome, failure.Errors, failure.Message);

            var existing = sensor.Policies.FirstOrDefault(p => p.Id == policyId);

            if (existing is null)
                return PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);

            if (existing.TemplateId is not null)
                return PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict, message: TemplateOwnedMessage);

            var initiator = InitiatorOf(user);

            var merged = sensor.Policies
                .Where(p => p.Id != policyId)
                .Select(p => new PolicyUpdate(p, initiator))
                .ToList();

            return SendAndVerifyRemoval(
                await _cache.UpdateSensorAsync(new SensorUpdate
                {
                    Id = sensorId,
                    Policies = merged,
                    Initiator = initiator,
                }),
                () => sensor.Policies.Any(p => p.Id == policyId));
        }

        // === TTL policies on a sensor ===

        /// <summary>
        /// Create one TTL policy on the sensor. The interval is explicit —
        /// <c>interval</c> or <c>inherit</c>, never a null: the server owns the
        /// reset-to-parent translation.
        /// </summary>
        public Task<PolicyWriteResult<TtlPolicyDto>> CreateSensorTtlPolicyAsync(Guid sensorId,
            TtlPolicyDto dto, ClaimsPrincipal user) =>
            LockedWriteAsync(ResolveWritableSensor(sensorId, user), () => CreateSensorTtlPolicyCoreAsync(sensorId, dto, user));

        private async Task<PolicyWriteResult<TtlPolicyDto>> CreateSensorTtlPolicyCoreAsync(Guid sensorId,
            TtlPolicyDto dto, ClaimsPrincipal user)
        {
            var (sensor, failure) = ResolveWritableSensor(sensorId, user);

            if (failure is not null)
                return PolicyWriteResult<TtlPolicyDto>.Fail(failure.Outcome, failure.Errors, failure.Message);

            // A JSON-null body reaches here only from MCP (REST binding
            // rejects it first): the field-keyed shape, never an NRE.
            if (dto is null)
                return PolicyWriteResult<TtlPolicyDto>.Fail(PolicyWriteOutcome.Invalid,
                    new Dictionary<string, string[]> { ["policy"] = ["The policy body is required."] });

            if (!TryValidateTtlDto(dto, AvailableChats(sensor), out var ttlTicks, out var errors))
                return PolicyWriteResult<TtlPolicyDto>.Fail(PolicyWriteOutcome.Invalid, errors);

            if (sensor.Policies.TTLPolicies.Count >= MaxPoliciesPerNode)
                return PolicyWriteResult<TtlPolicyDto>.Fail(PolicyWriteOutcome.Invalid, PolicyCountError("ttlPolicies"));

            var initiator = InitiatorOf(user);
            var policyId = Guid.NewGuid();

            var merged = sensor.Policies.TTLPolicies.Select(p => CopyTtlUpdate(p, initiator)).ToList();
            merged.Add(AlertPolicyDtoMapper.ToUpdate(dto, policyId, initiator, ttlTicks, AvailableChats(sensor)));

            return await SendSensorTtlUpdateAsync(sensor, policyId, merged, initiator);
        }

        /// <summary>Replace one TTL policy of the sensor; the interval/inherit switch is part of the content.</summary>
        public Task<PolicyWriteResult<TtlPolicyDto>> UpdateSensorTtlPolicyAsync(Guid sensorId,
            Guid policyId, TtlPolicyDto dto, ClaimsPrincipal user) =>
            LockedWriteAsync(ResolveWritableSensor(sensorId, user), () => UpdateSensorTtlPolicyCoreAsync(sensorId, policyId, dto, user));

        private async Task<PolicyWriteResult<TtlPolicyDto>> UpdateSensorTtlPolicyCoreAsync(Guid sensorId,
            Guid policyId, TtlPolicyDto dto, ClaimsPrincipal user)
        {
            var (sensor, failure) = ResolveWritableSensor(sensorId, user);

            if (failure is not null)
                return PolicyWriteResult<TtlPolicyDto>.Fail(failure.Outcome, failure.Errors, failure.Message);

            // A JSON-null body (MCP-only; REST binding rejects it first)
            // answers the field-keyed shape, never a NullReferenceException.
            if (dto is null)
                return PolicyWriteResult<TtlPolicyDto>.Fail(PolicyWriteOutcome.Invalid,
                    new Dictionary<string, string[]> { ["policy"] = ["The policy body is required."] });

            var existing = sensor.Policies.TTLPolicies.FirstOrDefault(p => p.Id == policyId);

            if (existing is null)
                return PolicyWriteResult<TtlPolicyDto>.Fail(PolicyWriteOutcome.NotFound);

            if (existing.TemplateId is not null && !IsPureTtlToggle(dto, existing))
                return PolicyWriteResult<TtlPolicyDto>.Fail(PolicyWriteOutcome.Conflict, message: TemplateOwnedMessage);

            // The TTL twin of the data-policy toggle skip (#1501 round-1, F3):
            // the core applies only the disable toggle for user-initiated
            // updates to template-owned TTL policies, so content validation
            // (which a template-minted body can fail — e.g. a no-effect echo)
            // would reject a change that never writes the content anyway.
            long? ttlTicks = null;

            if (existing.TemplateId is null &&
                !TryValidateTtlDto(dto, AvailableChats(sensor), out ttlTicks, out var errors))
                return PolicyWriteResult<TtlPolicyDto>.Fail(PolicyWriteOutcome.Invalid, errors);

            var initiator = InitiatorOf(user);

            var merged = sensor.Policies.TTLPolicies
                .Where(p => p.Id != policyId)
                .Select(p => CopyTtlUpdate(p, initiator))
                .ToList();

            // Template-owned toggle: the stored content and its EXPLICIT
            // interval ride through the copy-ctor shape with only IsDisabled
            // taken from the body — but as the CHANGED item it takes NORMAL
            // change ownership (ToggledTtlUpdate), unlike the untouched
            // siblings (CopyTtlUpdate).
            merged.Add(existing.TemplateId is not null
                ? ToggledTtlUpdate(existing, initiator, dto.IsDisabled)
                : AlertPolicyDtoMapper.ToUpdate(dto, policyId, initiator, ttlTicks, AvailableChats(sensor),
                    existing.TemplateId, existing.TemplateAlertId));

            return await SendSensorTtlUpdateAsync(sensor, policyId, merged, initiator);
        }

        /// <summary>Remove one TTL policy; every other TTL policy of the sensor rides through untouched.</summary>
        public Task<PolicyWriteResult> DeleteSensorTtlPolicyAsync(Guid sensorId, Guid policyId,
            ClaimsPrincipal user) =>
            LockedWriteAsync(ResolveWritableSensor(sensorId, user), () => DeleteSensorTtlPolicyCoreAsync(sensorId, policyId, user));

        private async Task<PolicyWriteResult> DeleteSensorTtlPolicyCoreAsync(Guid sensorId, Guid policyId,
            ClaimsPrincipal user)
        {
            var (sensor, failure) = ResolveWritableSensor(sensorId, user);

            if (failure is not null)
                return PolicyWriteResult.Fail(failure.Outcome, failure.Errors, failure.Message);

            var existing = sensor.Policies.TTLPolicies.FirstOrDefault(p => p.Id == policyId);

            if (existing is null)
                return PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);

            if (existing.TemplateId is not null)
                return PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict, message: TemplateOwnedMessage);

            var initiator = InitiatorOf(user);

            var merged = sensor.Policies.TTLPolicies
                .Where(p => p.Id != policyId)
                .Select(p => CopyTtlUpdate(p, initiator))
                .ToList();

            return SendAndVerifyRemoval(
                await _cache.UpdateSensorAsync(new SensorUpdate
                {
                    Id = sensorId,
                    TTLPolicies = merged,
                    Initiator = initiator,
                }),
                () => sensor.Policies.TTLPolicies.Any(p => p.Id == policyId));
        }

        // === Data policies under a product (the subtree aggregate) ===

        /// <summary>
        /// A product holds no data policies of its own (per-node alert creation
        /// was removed in #1142) — the product surface reads the subtree aggregate
        /// and routes writes to the owning sensor. Creation is therefore not
        /// expressible here and always answers 422 pointing at the sensor endpoint.
        /// </summary>
        public Task<PolicyWriteResult<ProductPolicyDto>> CreateProductPolicyAsync(Guid productId,
            PolicyDto dto, ClaimsPrincipal user) =>
            Task.FromResult(PolicyWriteResult<ProductPolicyDto>.Fail(PolicyWriteOutcome.Invalid,
                new Dictionary<string, string[]>
                {
                    ["policies"] =
                    [
                        "A product owns no data policies; create the policy on the owning sensor (POST /api/v1/sensors/{id}/policies)."
                    ],
                }));

        /// <summary>
        /// Replace one data policy found anywhere in the product's subtree; the
        /// write is applied atomically on the OWNING sensor's full list.
        /// </summary>
        // No product gate here (see _writeGates): the resolution below only
        // READS the aggregate to find the owner, and the delegated sensor-level
        // write takes the OWNING SENSOR's gate — the node whose list is actually
        // replaced. A policy never moves between sensors, so resolving outside
        // the gate cannot race the merge.
        public async Task<PolicyWriteResult<ProductPolicyDto>> UpdateProductPolicyAsync(Guid productId,
            Guid policyId, PolicyDto dto, ClaimsPrincipal user)
        {
            var resolution = ResolveProductPolicyWrite(productId, policyId, user);

            if (resolution.Failure is not null)
                return PolicyWriteResult<ProductPolicyDto>.Fail(resolution.Failure.Outcome,
                    resolution.Failure.Errors, resolution.Failure.Message);

            var result = await UpdateSensorPolicyAsync(resolution.Sensor.Id, policyId, dto, user);

            return result.Success
                ? PolicyWriteResult<ProductPolicyDto>.Ok(AlertPolicyDtoMapper.ToProductDto(
                    result.Value, resolution.Sensor))
                : PolicyWriteResult<ProductPolicyDto>.Fail(result.Failure.Outcome, result.Failure.Errors,
                    result.Failure.Message);
        }

        /// <summary>Remove one data policy found anywhere in the product's subtree, through the owning sensor's full list.</summary>
        public async Task<PolicyWriteResult> DeleteProductPolicyAsync(Guid productId, Guid policyId,
            ClaimsPrincipal user)
        {
            var resolution = ResolveProductPolicyWrite(productId, policyId, user);

            if (resolution.Failure is not null)
                return PolicyWriteResult.Fail(resolution.Failure.Outcome, resolution.Failure.Errors,
                    resolution.Failure.Message);

            return await DeleteSensorPolicyAsync(resolution.Sensor.Id, policyId, user);
        }

        // === TTL policies on a product (product-owned) ===

        /// <summary>Create one TTL policy on the product itself.</summary>
        public Task<PolicyWriteResult<TtlPolicyDto>> CreateProductTtlPolicyAsync(Guid productId,
            TtlPolicyDto dto, ClaimsPrincipal user) =>
            LockedWriteAsync(ResolveWritableProduct(productId, user), () => CreateProductTtlPolicyCoreAsync(productId, dto, user));

        private async Task<PolicyWriteResult<TtlPolicyDto>> CreateProductTtlPolicyCoreAsync(Guid productId,
            TtlPolicyDto dto, ClaimsPrincipal user)
        {
            var (product, failure) = ResolveWritableProduct(productId, user);

            if (failure is not null)
                return PolicyWriteResult<TtlPolicyDto>.Fail(failure.Outcome, failure.Errors, failure.Message);

            if (!TryValidateTtlDto(dto, AvailableChats(product), out var ttlTicks, out var errors))
                return PolicyWriteResult<TtlPolicyDto>.Fail(PolicyWriteOutcome.Invalid, errors);

            if (product.Policies.TTLPolicies.Count >= MaxPoliciesPerNode)
                return PolicyWriteResult<TtlPolicyDto>.Fail(PolicyWriteOutcome.Invalid, PolicyCountError("ttlPolicies"));

            var initiator = InitiatorOf(user);
            var policyId = Guid.NewGuid();

            var merged = product.Policies.TTLPolicies.Select(p => CopyTtlUpdate(p, initiator)).ToList();
            merged.Add(AlertPolicyDtoMapper.ToUpdate(dto, policyId, initiator, ttlTicks, AvailableChats(product)));

            return await SendProductTtlUpdateAsync(product.Id, policyId, merged, initiator);
        }

        /// <summary>Replace one TTL policy of the product; the interval/inherit switch is part of the content.</summary>
        public Task<PolicyWriteResult<TtlPolicyDto>> UpdateProductTtlPolicyAsync(Guid productId,
            Guid policyId, TtlPolicyDto dto, ClaimsPrincipal user) =>
            LockedWriteAsync(ResolveWritableProduct(productId, user), () => UpdateProductTtlPolicyCoreAsync(productId, policyId, dto, user));

        private async Task<PolicyWriteResult<TtlPolicyDto>> UpdateProductTtlPolicyCoreAsync(Guid productId,
            Guid policyId, TtlPolicyDto dto, ClaimsPrincipal user)
        {
            var (product, failure) = ResolveWritableProduct(productId, user);

            if (failure is not null)
                return PolicyWriteResult<TtlPolicyDto>.Fail(failure.Outcome, failure.Errors, failure.Message);

            var existing = product.Policies.TTLPolicies.FirstOrDefault(p => p.Id == policyId);

            if (existing is null)
                return PolicyWriteResult<TtlPolicyDto>.Fail(PolicyWriteOutcome.NotFound);

            if (existing.TemplateId is not null && !IsPureTtlToggle(dto, existing))
                return PolicyWriteResult<TtlPolicyDto>.Fail(PolicyWriteOutcome.Conflict, message: TemplateOwnedMessage);

            // The TTL toggle skip of the sensor twin (#1501 round-1, F3): the
            // core applies only the disable toggle for user-initiated updates
            // to template-owned TTL policies.
            long? ttlTicks = null;

            if (existing.TemplateId is null &&
                !TryValidateTtlDto(dto, AvailableChats(product), out ttlTicks, out var errors))
                return PolicyWriteResult<TtlPolicyDto>.Fail(PolicyWriteOutcome.Invalid, errors);

            var initiator = InitiatorOf(user);

            var merged = product.Policies.TTLPolicies
                .Where(p => p.Id != policyId)
                .Select(p => CopyTtlUpdate(p, initiator))
                .ToList();

            // The template-owned toggle takes NORMAL ownership here too (the
            // sensor twin above, #1501 round-3, F3).
            merged.Add(existing.TemplateId is not null
                ? ToggledTtlUpdate(existing, initiator, dto.IsDisabled)
                : AlertPolicyDtoMapper.ToUpdate(dto, policyId, initiator, ttlTicks, AvailableChats(product),
                    existing.TemplateId, existing.TemplateAlertId));

            return await SendProductTtlUpdateAsync(product.Id, policyId, merged, initiator);
        }

        /// <summary>Remove one TTL policy of the product; every other TTL policy rides through untouched.</summary>
        public Task<PolicyWriteResult> DeleteProductTtlPolicyAsync(Guid productId, Guid policyId,
            ClaimsPrincipal user) =>
            LockedWriteAsync(ResolveWritableProduct(productId, user), () => DeleteProductTtlPolicyCoreAsync(productId, policyId, user));

        private async Task<PolicyWriteResult> DeleteProductTtlPolicyCoreAsync(Guid productId, Guid policyId,
            ClaimsPrincipal user)
        {
            var (product, failure) = ResolveWritableProduct(productId, user);

            if (failure is not null)
                return PolicyWriteResult.Fail(failure.Outcome, failure.Errors, failure.Message);

            var existing = product.Policies.TTLPolicies.FirstOrDefault(p => p.Id == policyId);

            if (existing is null)
                return PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);

            if (existing.TemplateId is not null)
                return PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict, message: TemplateOwnedMessage);

            var initiator = InitiatorOf(user);

            var merged = product.Policies.TTLPolicies
                .Where(p => p.Id != policyId)
                .Select(p => CopyTtlUpdate(p, initiator))
                .ToList();

            return SendAndVerifyRemoval(
                await _cache.UpdateProductAsync(new ProductUpdate
                {
                    Id = productId,
                    TTLPolicies = merged,
                    Initiator = initiator,
                }),
                () => _cache.TryGetProduct(productId, out var live) && live.Policies.TTLPolicies.Any(p => p.Id == policyId));
        }


        // === Resolution and dispatch ===

        // The per-node write gate (#1501 round-1, F2 — see _writeGates): runs
        // one whole write — resolve, validate, merge, send, verify — under the
        // target node's semaphore, awaiting inside so callers keep their
        // asynchronous flow. Gates are never nested (the product data-policy
        // routing delegates BEFORE any gate is held).
        //
        // #1501 round-2 (F3): authorization and existence are resolved by the
        // CALLER, before this method runs — a 404/403 for an unknown or
        // invisible id must not allocate a per-node gate entry at all (a
        // read-write token spraying random GUIDs would otherwise leak one
        // gate per id forever). The write body itself re-resolves the node
        // UNDER the gate (the ResolveWritable* call that opens every *Core
        // method): the merge snapshot must be the state this write
        // serializes against, and a node removed and recreated between the
        // outside resolve and the gate must not be merged from its orphaned
        // predecessor.
        private async Task<PolicyWriteResult<T>> LockedWriteAsync<T>(
            (BaseNodeModel Node, PolicyWriteFailure Failure) resolved, Func<Task<PolicyWriteResult<T>>> write)
        {
            if (resolved.Failure is not null)
                return PolicyWriteResult<T>.Fail(resolved.Failure.Outcome, resolved.Failure.Errors, resolved.Failure.Message);

            var gate = AcquireWriteGate(resolved.Node.Id);

            await gate.WaitAsync().ConfigureAwait(false);

            try
            {
                return await write().ConfigureAwait(false);
            }
            finally
            {
                ReleaseWriteGate(resolved.Node.Id, gate);
            }
        }

        private async Task<PolicyWriteResult> LockedWriteAsync(
            (BaseNodeModel Node, PolicyWriteFailure Failure) resolved, Func<Task<PolicyWriteResult>> write)
        {
            if (resolved.Failure is not null)
                return PolicyWriteResult.Fail(resolved.Failure.Outcome, resolved.Failure.Errors, resolved.Failure.Message);

            var gate = AcquireWriteGate(resolved.Node.Id);

            await gate.WaitAsync().ConfigureAwait(false);

            try
            {
                return await write().ConfigureAwait(false);
            }
            finally
            {
                ReleaseWriteGate(resolved.Node.Id, gate);
            }
        }

        // Register as a gate user BEFORE waiting (see _writeGates): while a
        // writer WAITS it is already counted, so a concurrent release can
        // never observe a "last user leaving" that this writer is about to
        // invalidate.
        private SemaphoreSlim AcquireWriteGate(Guid nodeId)
        {
            lock (_gateSync)
            {
                if (!_writeGates.TryGetValue(nodeId, out var entry))
                    entry = (new SemaphoreSlim(1, 1), 0);

                _writeGates[nodeId] = (entry.Gate, entry.Users + 1);

                return entry.Gate;
            }
        }

        // Unregister FIRST, then release the semaphore: once the last user is
        // unregistered the entry is gone and an arriving writer creates a
        // FRESH gate — safe, because this user's critical section is already
        // over (we are in the finally); the old and new gates can never
        // overlap a write. Releasing the semaphore outside _gateSync keeps
        // waiter continuations off the registry lock.
        private void ReleaseWriteGate(Guid nodeId, SemaphoreSlim gate)
        {
            lock (_gateSync)
            {
                if (_writeGates.TryGetValue(nodeId, out var entry) && ReferenceEquals(entry.Gate, gate))
                {
                    if (entry.Users <= 1)
                        _writeGates.Remove(nodeId);
                    else
                        _writeGates[nodeId] = (gate, entry.Users - 1);
                }
            }

            gate.Release();
        }

        // Test probe (InternalsVisibleTo): live gate entries. Pins the
        // boundedness invariant (#1501 round-2, F3) — unknown ids allocate
        // nothing, idle gates leave the registry.
        internal int WriteGateCount
        {
            get { lock (_gateSync) return _writeGates.Count; }
        }

        // Authorization FIRST (the evaluator owns the 404/403 split), then the
        // live model lookup — an unknown id never reaches validation.
        private (BaseSensorModel Sensor, PolicyWriteFailure Failure) ResolveWritableSensor(Guid sensorId,
            ClaimsPrincipal user)
        {
            var failure = Authorize(user, SensorResource(sensorId));

            if (failure is not null)
                return (null, failure);

            var sensor = _cache.GetSensor(sensorId);

            return sensor is null ? (null, NotFound()) : (sensor, null);
        }

        private (ProductModel Product, PolicyWriteFailure Failure) ResolveWritableProduct(Guid productId,
            ClaimsPrincipal user)
        {
            var failure = Authorize(user, ProductResource(productId));

            if (failure is not null)
                return (null, failure);

            return !_cache.TryGetProduct(productId, out var product) || product is null
                ? (null, NotFound())
                : (product, null);
        }

        // The product data-policy surface resolves the OWNING sensor (the
        // subtree aggregate), authorizes the write at the product boundary and
        // then at the sensor's own boundary (the honest write target), and hands
        // the change to the sensor-level engine.
        private (BaseSensorModel Sensor, PolicyWriteFailure Failure) ResolveProductPolicyWrite(
            Guid productId, Guid policyId, ClaimsPrincipal user)
        {
            var productFailure = Authorize(user, ProductResource(productId));

            if (productFailure is not null)
                return (null, productFailure);

            if (!TryResolveProductPolicy(productId, policyId, out var sensor))
                return (null, NotFound());

            var sensorFailure = Authorize(user, SensorResource(sensor.Id));

            if (sensorFailure is not null)
                return (null, sensorFailure);

            return (sensor, null);
        }

        // The queue completes the in-memory update before its task resolves, so
        // post-update reads observe the stored state. A policy the core
        // silently refused (change-ownership protection) is reported honestly
        // as a conflict instead of echoing a ghost.
        private async Task<PolicyWriteResult<PolicyDto>> SendSensorUpdateAsync(BaseSensorModel sensor,
            Guid policyId, List<PolicyUpdate> merged, InitiatorInfo initiator)
        {
            var result = await _cache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Policies = merged,
                Initiator = initiator,
            });

            if (!result.IsOk)
                return PolicyWriteResult<PolicyDto>.Fail(PolicyWriteOutcome.Conflict, message: result.Error);

            var stored = sensor.Policies.FirstOrDefault(p => p.Id == policyId);

            return stored is null
                ? PolicyWriteResult<PolicyDto>.Fail(PolicyWriteOutcome.Conflict, message: NotAppliedMessage)
                : PolicyWriteResult<PolicyDto>.Ok(AlertPolicyDtoMapper.ToDto(stored, sensor.Type));
        }

        private async Task<PolicyWriteResult<TtlPolicyDto>> SendSensorTtlUpdateAsync(BaseSensorModel sensor,
            Guid policyId, List<PolicyUpdate> merged, InitiatorInfo initiator)
        {
            var result = await _cache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                TTLPolicies = merged,
                Initiator = initiator,
            });

            if (!result.IsOk)
                return PolicyWriteResult<TtlPolicyDto>.Fail(PolicyWriteOutcome.Conflict, message: result.Error);

            var stored = sensor.Policies.TTLPolicies.FirstOrDefault(p => p.Id == policyId);

            return stored is null
                ? PolicyWriteResult<TtlPolicyDto>.Fail(PolicyWriteOutcome.Conflict, message: NotAppliedMessage)
                : PolicyWriteResult<TtlPolicyDto>.Ok(AlertPolicyDtoMapper.ToDto(stored));
        }

        private async Task<PolicyWriteResult<TtlPolicyDto>> SendProductTtlUpdateAsync(Guid productId,
            Guid policyId, List<PolicyUpdate> merged, InitiatorInfo initiator)
        {
            var result = await _cache.UpdateProductAsync(new ProductUpdate
            {
                Id = productId,
                TTLPolicies = merged,
                Initiator = initiator,
            });

            if (!result.IsOk)
                return PolicyWriteResult<TtlPolicyDto>.Fail(PolicyWriteOutcome.Conflict, message: result.Error);

            if (!_cache.TryGetProduct(productId, out var product) || product is null ||
                product.Policies.TTLPolicies.FirstOrDefault(p => p.Id == policyId) is not { } stored)
                return PolicyWriteResult<TtlPolicyDto>.Fail(PolicyWriteOutcome.Conflict, message: NotAppliedMessage);

            return PolicyWriteResult<TtlPolicyDto>.Ok(AlertPolicyDtoMapper.ToDto(stored));
        }

        // Deletes verify the ABSENCE of the target after the update — the same
        // honest-reporting rule the echo paths apply, inverted.
        private static PolicyWriteResult SendAndVerifyRemoval(TaskResult result, Func<bool> stillPresent) =>
            !result.IsOk
                ? PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict, message: result.Error)
                : stillPresent()
                    ? PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict, message: NotAppliedMessage)
                    : PolicyWriteResult.Ok();

        private const string NotAppliedMessage =
            "The policy was accepted but not applied — the node's change ownership blocked it.";


        // === Validation ===

        // Semantic validation shared by both policy kinds: the destination and
        // notification contract, the schedule reference, the status effect, and
        // the at-least-one-effect rule. Condition validation (data policies
        // only) is type-aware and lives in AlertPolicyConditionValidator.
        private bool TryValidateDataDto(PolicyDto dto, SensorType sensorType, Guid sensorId,
            Dictionary<Guid, string> availableChats, out List<PolicyConditionUpdate> conditions,
            out IDictionary<string, string[]> errors)
        {
            var errorMap = new Dictionary<string, List<string>>();

            if (!AlertPolicyConditionValidator.TryBuildConditions(sensorType, sensorId, dto?.Conditions,
                    out conditions, out var conditionErrors))
                foreach (var (key, messages) in conditionErrors)
                    foreach (var message in messages)
                        AlertPolicyConditionValidator.Add(errorMap, key, message);

            ValidateEffects(dto?.Notification, dto?.Destination, dto?.Icon, dto?.Status, dto?.ScheduleId,
                dto?.ConfirmationPeriod, availableChats, errorMap);

            errors = ToErrors(errorMap);

            return errors.Count == 0;
        }

        private bool TryValidateTtlDto(TtlPolicyDto dto, Dictionary<Guid, string> availableChats,
            out long? ttlTicks, out IDictionary<string, string[]> errors)
        {
            var errorMap = new Dictionary<string, List<string>>();
            ttlTicks = null;

            // The inherit switch is EXCLUSIVE and exhaustive: exactly one of
            // interval / inherit must carry the intent — a null interval means
            // nothing on this surface (the server owns that translation).
            if (dto?.Inherit == true)
            {
                if (dto.Interval is not null)
                    AlertPolicyConditionValidator.Add(errorMap, "interval",
                        "interval and inherit are mutually exclusive; set exactly one.");
            }
            else if (string.IsNullOrEmpty(dto?.Interval))
            {
                AlertPolicyConditionValidator.Add(errorMap, "interval",
                    "The interval is required (a TimeSpan string, e.g. \"00:30:00\") unless inherit is true.");
            }
            else if (!TimeSpan.TryParse(dto.Interval, CultureInfo.InvariantCulture, out var parsed))
            {
                AlertPolicyConditionValidator.Add(errorMap, "interval",
                    "The interval must be a TimeSpan string, e.g. \"00:30:00\".");
            }
            else if (parsed <= TimeSpan.Zero)
            {
                AlertPolicyConditionValidator.Add(errorMap, "interval", "The interval must be positive.");
            }
            else
            {
                ttlTicks = parsed.Ticks;
            }

            ValidateEffects(dto?.Notification, dto?.Destination, dto?.Icon, dto?.Status, dto?.ScheduleId,
                confirmationPeriod: null, availableChats, errorMap);

            errors = ToErrors(errorMap);

            return errors.Count == 0;
        }

        private void ValidateEffects(AlertNotificationDto notification, AlertDestinationDto destination, string icon,
            string status, Guid? scheduleId, string confirmationPeriod, Dictionary<Guid, string> availableChats,
            Dictionary<string, List<string>> errors)
        {
            if (notification is not null)
            {
                if (string.IsNullOrWhiteSpace(notification.Template))
                    AlertPolicyConditionValidator.Add(errors, "notification.template",
                        "The notification template is required when a notification is present.");

                ValidateDestination(destination, availableChats, errors);

                if (!string.IsNullOrEmpty(notification.Repeat) &&
                    (!Enum.TryParse<AlertRepeatMode>(notification.Repeat, ignoreCase: true, out var repeat) ||
                     !Enum.IsDefined(repeat)))
                    AlertPolicyConditionValidator.Add(errors, "notification.repeat",
                        $"Unknown repeat mode '{notification.Repeat}'.");
            }

            if (status is not null && status is not "Error" and not "Ok")
                AlertPolicyConditionValidator.Add(errors, "status", "The status effect must be \"Error\" or \"Ok\".");

            if (scheduleId is { } schedule && _schedules.GetSchedule(schedule) is null)
                AlertPolicyConditionValidator.Add(errors, "scheduleId", $"Unknown schedule '{schedule}'.");

            if (confirmationPeriod is not null &&
                (!TimeSpan.TryParse(confirmationPeriod, CultureInfo.InvariantCulture, out var confirmation) ||
                 confirmation < TimeSpan.Zero))
                AlertPolicyConditionValidator.Add(errors, "confirmationPeriod",
                    "The confirmation period must be a TimeSpan string, e.g. \"00:05:00\".");

            // A policy with no effect at all would evaluate and never act — the
            // web editor cannot produce one (every row starts with a default
            // notification), so the API rejects it.
            if (notification is null && string.IsNullOrEmpty(icon) && status != "Error")
                AlertPolicyConditionValidator.Add(errors, "effects",
                    "At least one effect is required: a notification, an icon, or the Error status.");
        }

        private static void ValidateDestination(AlertDestinationDto destination,
            Dictionary<Guid, string> availableChats, Dictionary<string, List<string>> errors)
        {
            if (destination is null)
            {
                AlertPolicyConditionValidator.Add(errors, "destination",
                    "The destination is required when a notification is present.");

                return;
            }

            if (destination.Mode is null ||
                !KnownDestinationModes.Contains(destination.Mode, StringComparer.OrdinalIgnoreCase))
            {
                AlertPolicyConditionValidator.Add(errors, "destination.mode",
                    $"Unknown destination mode '{destination.Mode}'. Supported: {string.Join(", ", KnownDestinationModes)}.");

                return;
            }

            if (destination.Mode.Equals(nameof(PolicyDestinationMode.Custom), StringComparison.OrdinalIgnoreCase))
            {
                if (destination.Chats is not { Count: > 0 })
                {
                    AlertPolicyConditionValidator.Add(errors, "destination.chats",
                        "A custom destination requires at least one chat.");

                    return;
                }

                if (destination.Chats.Count > MaxChatsPerPolicy)
                {
                    AlertPolicyConditionValidator.Add(errors, "destination.chats",
                        $"At most {MaxChatsPerPolicy} chats per policy are allowed.");

                    return;
                }

                foreach (var chatId in destination.Chats)
                    if (!availableChats.ContainsKey(chatId))
                        AlertPolicyConditionValidator.Add(errors, "destination.chats",
                            $"Chat '{chatId}' is not available on this node.");
            }
            else if (destination.Chats is { Count: > 0 })
            {
                AlertPolicyConditionValidator.Add(errors, "destination.chats",
                    "Chats are only accepted for the Custom destination mode.");
            }

            // An explicit "chats": null is coherent everywhere with an empty
            // array (#1501 round-3, F2): rejected for Custom above (the
            // at-least-one-chat rule), accepted as no-chats for the other
            // modes — and mapped to no chats by AlertPolicyDtoMapper.
        }

        // A template-owned policy accepts ONLY the disable toggle through a user
        // initiator (the core's own rule — everything else is silently ignored
        // there). The API answers 409 instead of silently dropping content: the
        // request must be equivalent to the policy's CURRENT content with only
        // isDisabled different. The comparison renders the EXISTING policy
        // through the same DTO mapper, so equality is judged on the API's own
        // expression of the content (a policy holding fields outside the API's
        // range never reads equal to an API-shaped body — the conservative
        // direction: 409, not a silent content drop).
        private static bool IsPureDataToggle(PolicyDto dto, Policy existing)
        {
            var current = AlertPolicyDtoMapper.ToDto(existing, existing.Sensor?.Type ?? SensorType.String);

            return dto is not null &&
                   ConditionsEqual(dto.Conditions, current.Conditions) &&
                   StringEquals(dto.ConfirmationPeriod, current.ConfirmationPeriod) &&
                   EffectsEqual(dto.Notification, current.Notification, dto.Destination, current.Destination,
                       dto.Icon, current.Icon, dto.Status, current.Status, dto.ScheduleId, current.ScheduleId);
        }

        private static bool IsPureTtlToggle(TtlPolicyDto dto, TTLPolicy existing)
        {
            var current = AlertPolicyDtoMapper.ToDto(existing);

            return dto is not null &&
                   dto.Inherit == current.Inherit &&
                   StringEquals(dto.Interval, current.Interval) &&
                   EffectsEqual(dto.Notification, current.Notification, dto.Destination, current.Destination,
                       dto.Icon, current.Icon, dto.Status, current.Status, dto.ScheduleId, current.ScheduleId);
        }

        private static bool ConditionsEqual(List<AlertConditionDto> requested, List<AlertConditionDto> current)
        {
            if (requested is null || current is null || requested.Count != current.Count)
                return false;

            for (var i = 0; i < requested.Count; i++)
            {
                if (!StringEquals(requested[i]?.Property, current[i]?.Property) ||
                    !StringEquals(requested[i]?.Operation, current[i]?.Operation) ||
                    !StringEquals(requested[i]?.Combination, current[i]?.Combination, "And") ||
                    !TargetEquals(requested[i]?.Target, current[i]?.Target))
                    return false;
            }

            return true;
        }

        private static bool EffectsEqual(AlertNotificationDto requestedNotification, AlertNotificationDto currentNotification,
            AlertDestinationDto requestedDestination, AlertDestinationDto currentDestination,
            string requestedIcon, string currentIcon, string requestedStatus, string currentStatus,
            Guid? requestedScheduleId, Guid? currentScheduleId)
        {
            var notificationEqual =
                (requestedNotification is null) == (currentNotification is null) &&
                (requestedNotification is null ||
                 (StringEquals(requestedNotification.Template, currentNotification.Template) &&
                  StringEquals(requestedNotification.Repeat, currentNotification.Repeat, "Immediately") &&
                  requestedNotification.InstantSend == currentNotification.InstantSend &&
                  Nullable.Equals(requestedNotification.StartAt, currentNotification.StartAt)));

            var destinationEqual =
                StringEquals(requestedDestination?.Mode, currentDestination?.Mode) &&
                ChatSetEquals(requestedDestination?.Chats, currentDestination?.Chats);

            return notificationEqual &&
                   destinationEqual &&
                   StringEquals(requestedIcon, currentIcon) &&
                   StringEquals(requestedStatus, currentStatus, "Ok") &&
                   Nullable.Equals(requestedScheduleId, currentScheduleId);
        }

        // Null and empty are the SAME chat set (#1501 round-3, F2): an echoed
        // body carrying "chats": null must read equal to the [] a GET
        // rendered, or a pure disable toggle would 409 over the null.
        private static bool ChatSetEquals(List<Guid> left, List<Guid> right)
        {
            left ??= [];
            right ??= [];

            return (left.Count == 0 && right.Count == 0) || new HashSet<Guid>(left).SetEquals(right);
        }

        // Targets arrive as System.Text.Json elements on the request side and as
        // parsed CLR values on the read side; compare their canonical string
        // forms (the same invariant serialization the validator produces).
        private static bool TargetEquals(object requested, object current) =>
            ReferenceEquals(requested, current) ||
            (requested is { } r && current is { } c &&
             string.Equals(AlertPolicyDtoMapper.ToCanonicalTargetString(r),
                 AlertPolicyDtoMapper.ToCanonicalTargetString(c), StringComparison.Ordinal));

        private static bool StringEquals(string left, string right, string defaultWhenNull = null) =>
            string.Equals(left ?? defaultWhenNull, right ?? defaultWhenNull, StringComparison.OrdinalIgnoreCase);


        // === Lookups and shared helpers ===

        private PolicyWriteFailure Authorize(ClaimsPrincipal user, ApiTokenResource resource)
        {
            var decision = _authorization.AuthorizeWrite(user, resource);

            return decision switch
            {
                ApiTokenAuthorization.Allowed => null,
                ApiTokenAuthorization.Forbidden => new PolicyWriteFailure
                {
                    Outcome = PolicyWriteOutcome.Forbidden,
                    Message = "The token is read-only or the token's owner cannot write at this target.",
                },
                _ => NotFound(),
            };
        }

        private static PolicyWriteFailure NotFound() => new() { Outcome = PolicyWriteOutcome.NotFound };

        private static ApiTokenResource SensorResource(Guid sensorId) =>
            new(ApiTokenResourceKind.Sensor, sensorId);

        private static ApiTokenResource ProductResource(Guid productId) =>
            new(ApiTokenResourceKind.Product, productId);

        private static Dictionary<string, string[]> PolicyCountError(string field) =>
            new() { [field] = [$"At most {MaxPoliciesPerNode} policies per node are allowed."] };

        // The subtree aggregate behind the product data-policy surface: the
        // owning sensor of a policy id, when that policy lives on a sensor under
        // the product.
        private bool TryResolveProductPolicy(Guid productId, Guid policyId, out BaseSensorModel sensor)
        {
            sensor = null;

            if (!_cache.TryGetProduct(productId, out var product) || product is null)
                return false;

            foreach (var candidate in product.GetAllSensors())
            {
                if (candidate.Policies.Any(p => p.Id == policyId))
                {
                    sensor = candidate;
                    return true;
                }
            }

            return false;
        }

        // The stored interval of a re-asserted TTL policy, EXPLICITLY: a null
        // TTL in full-list semantics is an explicit reset-to-parent (the
        // #1409/#1451 lesson — without this every merge would reset every
        // other TTL policy).
        private static long? StoredTtlTicks(TTLPolicy policy) =>
            policy.IsTTLFromParent || policy.TTLInterval is not { IsNone: false } interval
                ? null
                : interval.Ticks;

        // An UNTOUCHED sibling re-asserted by a full-list merge: it must
        // PRESERVE its change-table ownership (#1501 round-2, F2) — the TTL
        // stamp loop in BaseNodeModel.Update stamps unconditionally per id, so
        // a plain copy re-asserted by an API user would re-stamp a
        // template-applied sibling as the calling user — and the next template
        // apply (AlertTemplate, type 15) would then fail the node's CanChange
        // pre-check (100 <= 15 is false) for the WHOLE node.
        // PreserveChangeOwnership opts the copy out of the stamp.
        private static PolicyUpdate CopyTtlUpdate(TTLPolicy policy, InitiatorInfo initiator) =>
            new(policy, initiator)
            {
                TTL = StoredTtlTicks(policy),
                PreserveChangeOwnership = true,
            };

        // The CHANGED item of a template-owned TTL toggle (#1501 round-3, F3):
        // the stored content and interval ride through like a sibling copy,
        // but WITHOUT PreserveChangeOwnership — the toggle is the calling
        // user's change, so the change table stamps the user as the owner (the
        // data-policy twin stamps through the CallJournal IsDisabled arm).
        // Ownership is the point: the CanChange pre-check then protects the
        // user's disable from the next template apply re-enabling it.
        private static PolicyUpdate ToggledTtlUpdate(TTLPolicy policy, InitiatorInfo initiator, bool isDisabled) =>
            new(policy, initiator)
            {
                TTL = StoredTtlTicks(policy),
                IsDisabled = isDisabled,
            };

        // The node's chat availability: every global chat plus the chats bound
        // to the folder of the node's root product — GetAvailableChats, the same
        // predicate the destination pickers apply. Parentless nodes resolve no
        // folder (the evaluator 404s them long before this runs; this stays
        // null-safe for the mocked-evaluator tests).
        private Dictionary<Guid, string> AvailableChats(BaseNodeModel node)
        {
            HashSet<Guid> folderChats = [];

            var root = node switch
            {
                ProductModel { Parent: null } rootProduct => rootProduct,
                ProductModel nested => nested.Root,
                BaseSensorModel { Parent: not null } sensor => sensor.Parent?.Root,
                _ => null,
            };

            if (root?.FolderId is { } folderId && _folders.TryGetValue(folderId, out var folder))
                folder.TryGetChats(out folderChats);

            return folderChats.GetAvailableChats(_chats);
        }

        // The write initiator mirrors the web UI's CurrentInitiator: the token's
        // OWNER as a user (the token acts as its owner, #1384).
        private InitiatorInfo InitiatorOf(ClaimsPrincipal user)
        {
            var ownerClaim = user?.FindFirst(HsmApiTokenClaims.OwnerUserId)?.Value;

            var owner = Guid.TryParse(ownerClaim, out var ownerId) ? _users[ownerId] : null;

            return InitiatorInfo.AsUser(owner?.Name ?? "api-token");
        }

        private static Dictionary<string, string[]> ToErrors(Dictionary<string, List<string>> errorMap) =>
            errorMap.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }
}
