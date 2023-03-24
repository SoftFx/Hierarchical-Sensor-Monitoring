using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public abstract class BaseNodeModel
    {
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
            AuthorId = Guid.Empty;
            CreationDate = DateTime.UtcNow;

            ServerPolicy.ExpectedUpdate.Uploaded += (_, _) => HasUpdateTimeout();
        }

        protected BaseNodeModel(string name) : this()
        {
            DisplayName = name;
        }

        protected BaseNodeModel(BaseNodeEntity entity) : this()
        {
            Id = Guid.Parse(entity.Id);
            AuthorId = Guid.TryParse(entity.AuthorId, out var authorId) ? authorId : null;
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
            Description = update.Description ?? Description;

            if (update.ExpectedUpdateInterval != null)
                ServerPolicy.ExpectedUpdate.SetPolicy(update.ExpectedUpdateInterval);

            var restoreInterval = update.RestoreInterval;

            if (restoreInterval != null)
            {
                ServerPolicy.RestoreError.SetPolicy(restoreInterval);
                ServerPolicy.RestoreWarning.SetPolicy(restoreInterval);
                ServerPolicy.RestoreOffTime.SetPolicy(restoreInterval);
            }
        }


        internal abstract bool HasUpdateTimeout();

        internal virtual void AddPolicy<T>(T policy) where T : Policy => ServerPolicy.ApplyPolicy(policy);

        protected virtual List<Guid> GetPolicyIds() => ServerPolicy.GetIds().ToList();

        internal void ApplyPolicies(List<string> policyIds, Dictionary<string, Policy> allPolicies)
        {
            foreach (var id in policyIds ?? Enumerable.Empty<string>())
                if (allPolicies.TryGetValue(id, out var policy))
                    AddPolicy(policy);
        }
    }
}
