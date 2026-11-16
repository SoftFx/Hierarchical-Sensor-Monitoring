using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Extensions;
using HSMServer.Core.Journal;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace HSMServer.Core.Model
{
    public abstract class BaseNodeModel : IChangesEntity
    {
        private readonly List<PolicyEntity> _ttlEntities;


        internal ChangeInfoTable ChangeTable { get; }

        public abstract BaseSettingsCollection Settings { get; }

        public abstract PolicyCollectionBase Policies { get; }


        public Guid Id { get; }

        public Guid? AuthorId { get; }

        public DateTime CreationDate { get; }


        public ProductModel Parent { get; private set; }


        public string DisplayName { get; protected set; }

        public string Description { get; private set; }


        public bool IsRoot => Parent is null;

        public string FullPath => IsRoot ? $"{DisplayName}" : $"{Parent.FullPath}/{DisplayName}";

        public string Path => IsRoot ? string.Empty : Parent.IsRoot ? DisplayName : $"{Parent.Path}/{DisplayName}";

        public string RootProductName => Parent?.RootProductName ?? DisplayName;

        public ProductModel Root => Parent?.Root ?? (ProductModel)this;


        public event Action<JournalRecordModel> ChangesHandler;


        protected BaseNodeModel()
        {
            ChangeTable = new ChangeInfoTable(() => FullPath);

            Id = Guid.NewGuid();
            CreationDate = DateTime.UtcNow;

            _ttlEntities = [];
        }

        protected BaseNodeModel(string name, Guid? authorId) : this()
        {
            DisplayName = name;
            AuthorId = authorId ?? Guid.Empty;
        }

        protected BaseNodeModel(BaseNodeEntity entity) : this()
        {
            // Migration: handle both old single TTLPolicy and new TTLPolicies list
            _ttlEntities = entity.TTLPolicies?.Count > 0
                ? entity.TTLPolicies
                : entity.TTLPolicy != null ? [entity.TTLPolicy] : [];

            Id = Guid.Parse(entity.Id);
            AuthorId = Guid.TryParse(entity.AuthorId, out var authorId) ? authorId : Guid.Empty;
            CreationDate = new DateTime(entity.CreationDate);

            DisplayName = entity.DisplayName;
            Description = entity.Description;

            ChangeTable.FromEntity(entity.ChangeTable);
        }


        internal abstract bool CheckTimeout();

        protected abstract void UpdateTTLs(List<PolicyUpdate> updates, InitiatorInfo initiator);


        internal BaseNodeModel AddParent(ProductModel parent)
        {
            Parent = parent;

            Settings.SetParentSettings(parent.Settings);
            Policies.BuildDefault(this, _ttlEntities); //need for correct calculating $product and $path properties
            ChangeTable.MigrateLegacyTtlKey(Policies.TTLPolicies.Select(p => p.Id).ToList());

            return this;
        }

        // Sensor move (#1718): re-parent and rename WITHOUT AddParent's policy
        // rebuild — AddParent rebuilds the TTL policies from the entity the
        // node was loaded with, which would discard every policy change made
        // since. Settings re-point at the new parent's, so "From parent"
        // values resolve there from now on.
        internal void MoveTo(ProductModel newParent, string newName)
        {
            Parent = newParent;
            DisplayName = newName;

            Settings.SetParentSettings(newParent.Settings);
        }

        protected void Update(BaseNodeUpdate update)
        {
            Description = UpdateProperty(Description, update.Description ?? Description, update.Initiator);

            Settings.Update(update, ChangeTable);

            if (update.TTLPolicies is not null)
            {
                var canChange = true;
                foreach (var ttlUpdate in update.TTLPolicies)
                {
                    var key = ttlUpdate.Id.ToString();
                    if (ttlUpdate.Id != Guid.Empty && !ChangeTable.TtlPolicies[key].CanChange(update.Initiator))
                    {
                        // A toggle-preserving entry carries the operator's
                        // stored IsDisabled and must not abort the whole TTL
                        // list update. Only a USER owner marks an operator's
                        // toggle; any other higher-priority owner is
                        // machinery that a template apply reclaims its entry
                        // from instead of preserving it.
                        if (ttlUpdate.PreservesOperatorToggle ||
                            (update.Initiator.IsTemplateApply && ChangeTable.TtlPolicies[key].Initiator.Type != InitiatorType.User))
                            continue;

                        canChange = false;
                        break;
                    }
                }

                if (canChange)
                {
                    var updateIds = new HashSet<string>(
                        update.TTLPolicies
                            .Where(u => u.Id != Guid.Empty)
                            .Select(u => u.Id.ToString()));

                    // A template apply carries only its own TTL entries
                    // (UpdateTTLs preserves every other TTL of the node
                    // untouched), so it must not re-stamp the change-table
                    // owner of entries it did not list. Every other update
                    // keeps the full-list stamp, which respects change
                    // ownership: a non-force initiator never re-stamps a
                    // non-listed entry whose current owner outranks it
                    // (CanChange) — e.g. a collector re-registration must
                    // not release a User toggle marker from a template-owned
                    // TTL it preserves verbatim.
                    if (!update.Initiator.IsTemplateApply)
                        foreach (var existing in Policies.TTLPolicies)
                        {
                            var existingKey = existing.Id.ToString();

                            if (!updateIds.Contains(existingKey) &&
                                ChangeTable.TtlPolicies[existingKey].CanChange(update.Initiator))
                                ChangeTable.TtlPolicies[existingKey].SetUpdate(update.Initiator);
                        }

                    UpdateTTLs(update.TTLPolicies, update.Initiator);

                    foreach (var ttlUpdate in update.TTLPolicies)
                    {
                        // #1409/#1451: updates carrying
                        // PreserveChangeOwnership (the schedule detach, the
                        // folder chat removal, the #1077 toggle-preserving
                        // template re-apply) do not take ownership — a
                        // re-stamp here would downgrade a recorded User owner
                        // and release the change-table marker that keeps the
                        // operator's disable toggle preserved. Every OTHER
                        // update stamps its targeted policies unconditionally,
                        // including edits the change journal does not render.
                        // This loop stamps TTL policies only; the
                        // regular-policy counterpart of the opt-out is the
                        // stampOwnership gate on CallJournal in
                        // SensorPolicyCollection.TryUpdate.
                        if (ttlUpdate.Id == Guid.Empty || ttlUpdate.PreserveChangeOwnership)
                            continue;

                        ChangeTable.TtlPolicies[ttlUpdate.Id.ToString()].SetUpdate(update.Initiator);
                    }
                }
            }

            CheckTimeout();
        }


        // The informational root product id of the journal record (#1588
        // review round 1): Root casts THIS to ProductModel when the node has
        // no parent, which throws for a detached sensor — display metadata
        // must never fail the write.
        private Guid? RootProductIdOf() => this is ProductModel product
            ? (product.Root ?? product).Id
            : Parent?.Root?.Id ?? Parent?.Id;

        protected T UpdateProperty<T>(T oldValue, T newValue, InitiatorInfo initiator, [CallerArgumentExpression(nameof(oldValue))] string propName = "", bool? forced = null, SensorUpdate update = null, BaseSensorModel oldModel = null)
        {
            var infoNode = ChangeTable.Properties[propName];
            var forceUpdate = forced ?? initiator.IsForceUpdate;

            if (!forceUpdate && !infoNode.CanChange(initiator))
                return oldValue;

            if (newValue is not null && !newValue.Equals(oldValue ?? newValue))
            {
                ChangesHandler?.Invoke(new JournalRecordModel(Id, initiator)
                {
                    Enviroment = "General info update",
                    OldValue = GetSpecificValue(oldValue),
                    NewValue = GetSpecificValue(newValue),

                    PropertyName = propName,
                    Path = FullPath,
                    Kind = JournalKinds.NodeSettings,
                    ProductId = RootProductIdOf()?.ToString(),
                    ChangeSetId = initiator?.ChangeSetId?.ToString(),
                });

                infoNode.SetUpdate(initiator);
            }

            return newValue ?? oldValue;

            string GetSpecificValue(T value)
            {
                if (update is null || oldModel is null)
                    return value.ToString();

                return value switch
                {
                    SensorState state => state is SensorState.Muted ? $"{state} until {(update.EndOfMutingPeriod ?? oldModel.EndOfMuting)?.ToDefaultFormat()}" : state.ToString(),
                    _ => value.ToString()
                };
            }
        }
    }
}
