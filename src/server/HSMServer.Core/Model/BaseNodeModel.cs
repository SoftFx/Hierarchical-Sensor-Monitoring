using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public abstract class BaseNodeModel
    {
        public SettingsCollection Settings { get; } = new();


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

            Settings.TTL.Uploaded += (_, _) => HasUpdateTimeout();
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

            Settings.SetSettings(entity.Settings);
        }


        protected internal BaseNodeModel AddParent(ProductModel parent)
        {
            Parent = parent;

            Settings.SetParentSettings(parent.Settings);

            return this;
        }

        protected internal void Update(BaseNodeUpdate update)
        {
            Description = update.Description ?? Description;

            if (update.TTL != null)
                Settings.TTL.SetValue(update.TTL);

            if (update.KeepHistory != null)
                Settings.KeepHistory.SetValue(update.KeepHistory);

            if (update.SelfDestroy != null)
                Settings.SelfDestroy.SetValue(update.SelfDestroy);

            var restoreInterval = update.RestoreInterval;

            //if (restoreInterval != null)
            //{
            //    ServerPolicy.RestoreError.SetPolicy(restoreInterval);
            //    ServerPolicy.RestoreWarning.SetPolicy(restoreInterval);
            //    ServerPolicy.RestoreOffTime.SetPolicy(restoreInterval);
            //}
        }


        internal abstract bool HasUpdateTimeout();

        internal abstract void AddPolicy<T>(T policy) where T : Policy;

        //internal virtual List<Guid> GetPolicyIds() => ServerPolicy.Ids.ToList();

        internal void ApplyPolicies(List<string> policyIds, Dictionary<string, Policy> allPolicies)
        {
            foreach (var id in policyIds ?? Enumerable.Empty<string>())
                if (allPolicies.TryGetValue(id, out var policy))
                    AddPolicy(policy);
        }
    }
}
