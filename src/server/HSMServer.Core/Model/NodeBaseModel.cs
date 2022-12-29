using HSMCommon.Constants;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public abstract class NodeBaseModel
    {
        public Guid Id { get; protected set; }

        public Guid? AuthorId { get; protected set; }

        public string DisplayName { get; protected set; }

        public string Description { get; protected set; }

        public DateTime CreationDate { get; protected set; }

        public ProductModel ParentProduct { get; internal set; }


        /// <summary>
        /// Product ID that is parent for this node and doesn't have parent product (top level product)
        /// </summary>
        public Guid RootProductId { get; protected set; }

        public string RootProductName { get; protected set; }

        public string Path { get; protected set; }


        public ExpectedUpdateIntervalPolicy UsedExpectedUpdateInterval =>
            ExpectedUpdateInterval ?? ParentProduct?.UsedExpectedUpdateInterval;

        public ExpectedUpdateIntervalPolicy ExpectedUpdateInterval { get; internal set; }


        internal virtual void BuildProductNameAndPath()
        {
            RootProductId = ParentProduct.RootProductId;
            RootProductName = ParentProduct.RootProductName;
            Path = $"{ParentProduct.Path}{CommonConstants.SensorPathSeparator}{DisplayName}";
        }


        internal abstract void RefreshOutdatedError();


        internal void ApplyPolicies(List<string> entityPolicies, Dictionary<Guid, Policy> allPolicies)
        {
            if (entityPolicies != null)
                foreach (var (_, policy) in allPolicies.IntersectBy(entityPolicies, k => k.Key.ToString()))
                    AddPolicy(policy);
        }

        internal virtual void AddPolicy(Policy policy)
        {
            if (policy is ExpectedUpdateIntervalPolicy expectedUpdateIntervalPolicy)
                ExpectedUpdateInterval = expectedUpdateIntervalPolicy;
        }

        protected virtual List<string> GetPolicyIds()
        {
            var policies = new List<string>(1 << 2);

            if (ExpectedUpdateInterval != null)
                policies.Add(ExpectedUpdateInterval.Id.ToString());

            return policies;
        }
    }
}
