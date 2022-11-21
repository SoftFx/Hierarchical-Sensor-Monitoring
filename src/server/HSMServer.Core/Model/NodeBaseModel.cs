using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public abstract class NodeBaseModel
    {
        public Guid? AuthorId { get; protected set; }

        public string DisplayName { get; protected set; }

        public string Description { get; protected set; }

        public DateTime CreationDate { get; protected set; }

        public ProductModel ParentProduct { get; internal set; }


        /// <summary>
        /// Product ID that is parent for this node and doesn't have parent product (top level product)
        /// </summary>
        public string RootProductId { get; protected set; }

        public string RootProductName { get; protected set; }

        public string Path { get; protected set; }


        public ExpectedUpdateIntervalPolicy UsedExpectedUpdateIntervalPolicy =>
            ExpectedUpdateIntervalPolicy ?? ParentProduct?.UsedExpectedUpdateIntervalPolicy;

        public ExpectedUpdateIntervalPolicy ExpectedUpdateIntervalPolicy { get; internal set; }


        internal virtual void BuildProductNameAndPath()
        {
            RootProductId = ParentProduct.RootProductId;
            RootProductName = ParentProduct.RootProductName;
        }


        internal abstract void UpdateExpectedUpdateIntervalError();


        internal void ApplyPolicies(List<string> entityPolicies, Dictionary<Guid, Policy> allPolicies)
        {
            if (entityPolicies != null)
                foreach (var (_, policy) in allPolicies.IntersectBy(entityPolicies, k => k.Key.ToString()))
                    AddPolicy(policy);
        }

        internal virtual void AddPolicy(Policy policy)
        {
            if (policy is ExpectedUpdateIntervalPolicy expectedUpdateIntervalPolicy)
                ExpectedUpdateIntervalPolicy = expectedUpdateIntervalPolicy;
        }

        protected virtual List<string> GetPolicyIds()
        {
            var policies = new List<string>(1 << 2);

            if (ExpectedUpdateIntervalPolicy != null)
                policies.Add(ExpectedUpdateIntervalPolicy.Id.ToString());

            return policies;
        }
    }
}
