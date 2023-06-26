using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace HSMServer.Core.Model
{
    public abstract class BaseNodeModel
    {
        public List<JournalRecordModel> JournalRecordModels { get; set; } = new();

        public ServerPolicyCollection ServerPolicy { get; } = new();

        public Guid Id { get; }

        public Guid? AuthorId { get; }

        public DateTime CreationDate { get; }


        public ProductModel Parent { get; private set; }


        public string DisplayName { get; private set; }

        public string Description { get; private set; }


        public string RootProductName => Parent?.RootProductName ?? DisplayName;

        public string Path => Parent is null ? string.Empty : $"{Parent.Path}/{DisplayName}";


        protected BaseNodeModel()
        {
            Id = Guid.NewGuid();
            CreationDate = DateTime.UtcNow;

            ServerPolicy.ExpectedUpdate.Uploaded += (_, _) => HasUpdateTimeout();
        }

        protected BaseNodeModel(string name, Guid? authorId) : this()
        {
            DisplayName = name;
            AuthorId = authorId ?? Guid.Empty;
        }

        protected BaseNodeModel(BaseNodeEntity entity) : this()
        {
            Id = Guid.Parse(entity.Id);
            AuthorId = Guid.TryParse(entity.AuthorId, out var authorId) ? authorId : Guid.Empty;
            CreationDate = new DateTime(entity.CreationDate);

            DisplayName = entity.DisplayName;
            Description = entity.Description;
        }


        protected internal BaseNodeModel AddParent(ProductModel parent)
        {
            Parent = parent;

            ServerPolicy.ApplyParentPolicies(parent.ServerPolicy);

            return this;
        }

        protected internal void Update(BaseNodeUpdate update)
        {
            Description = ApplyUpdate(Description, update.Description);

            if (update.ExpectedUpdateInterval != null)
                ServerPolicy.ExpectedUpdate.SetPolicy(update.ExpectedUpdateInterval);
            
            var restoreInterval = update.RestoreInterval;

            if (restoreInterval != null)
            {
                ServerPolicy.RestoreError.SetPolicy(restoreInterval);
                ServerPolicy.RestoreWarning.SetPolicy(restoreInterval);
                ServerPolicy.RestoreOffTime.SetPolicy(restoreInterval);
            }

            if (update.SavedHistoryPeriod != null)
                ServerPolicy.SavedHistoryPeriod.SetPolicy(update.SavedHistoryPeriod);

            if (update.SelfDestroy != null)
                ServerPolicy.SelfDestroy.SetPolicy(update.SelfDestroy);
        }


        internal abstract bool HasUpdateTimeout();

        internal virtual void AddPolicy<T>(T policy) where T : Policy => ServerPolicy.ApplyPolicy(policy);

        internal virtual List<Guid> GetPolicyIds() => ServerPolicy.Ids.ToList();

        internal void ApplyPolicies(List<string> policyIds, Dictionary<string, Policy> allPolicies)
        {
            foreach (var id in policyIds ?? Enumerable.Empty<string>())
                if (allPolicies.TryGetValue(id, out var policy))
                    AddPolicy(policy);
        }
        
        
        internal T ApplyUpdate<T>(T property, T update, [CallerArgumentExpression("property")] string propertyName = null)
        {
            if (update is not null && !update.Equals(property))
            {
                JournalRecordModels.Add(new JournalRecordModel()
                {
                    Id = Id,
                    Time = DateTime.UtcNow.Ticks,
                    Value = $"{propertyName}: {property} -> {update}"
                });
                
                return update;   
            }

            return property;
        }
    }
}
