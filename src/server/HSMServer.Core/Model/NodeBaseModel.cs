using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public abstract class NodeBaseModel
    {
        public ServerPolicyCollection ServerPolicy { get; } = new();


        public Guid Id { get; }

        public Guid? AuthorId { get; }

        public DateTime CreationDate { get; }


        public ProductModel ParentProduct { get; private set; }


        public string DisplayName { get; private set; }

        public string Description { get; private set; }



        public string RootProductName => ParentProduct?.RootProductName ?? DisplayName;

        public Guid RootProductId => ParentProduct?.RootProductId ?? Id;

        public string Path => ParentProduct is null ? string.Empty : $"{ParentProduct.Path}/{DisplayName}";


        protected NodeBaseModel()
        {
            Id = Guid.NewGuid();
            AuthorId = Guid.Empty;
            CreationDate = DateTime.UtcNow;
        }

        protected NodeBaseModel(string name) : this()
        {
            DisplayName = name;
        }

        protected NodeBaseModel(BaseNodeEntity entity)
        {
            Id = Guid.Parse(entity.Id);
            AuthorId = Guid.TryParse(entity.AuthorId, out var authorId) ? authorId : null;
            CreationDate = new DateTime(entity.CreationDate);

            DisplayName = entity.DisplayName;
            Description = entity.Description;
        }


        protected internal NodeBaseModel AddParent(ProductModel parent)
        {
            ParentProduct = parent;

            ServerPolicy.ApplyParentPolicies(parent.ServerPolicy);

            return this;
        }

        protected internal void Update(BaseNodeUpdate upadate)
        {
            Description = upadate.Description ?? Description;
        }


        internal abstract bool HasServerValidationChange();

        internal virtual void AddPolicy<T>(T policy) where T: Policy => ServerPolicy.ApplyPolicy(policy);

        protected virtual List<Guid> GetPolicyIds() => ServerPolicy.ToList();

        internal void ApplyPolicies(List<string> policyIds, Dictionary<string, Policy> allPolicies)
        {
            foreach (var id in policyIds ?? Enumerable.Empty<string>())
                if (allPolicies.TryGetValue(id, out var policy))
                    AddPolicy(policy);
        }
    }
}
