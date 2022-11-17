using HSMCommon.Constants;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public abstract class NodeBaseModel
    {
        private ExpectedUpdateIntervalPolicy _expectedUpdateIntervalPolicy;


        public Guid? AuthorId { get; protected set; }

        public string DisplayName { get; protected set; }

        public string Description { get; protected set; }

        public DateTime CreationDate { get; protected set; }

        public ProductModel ParentProduct { get; internal set; }


        /// <summary>
        /// Product ID that is parent for this node and doesn't have parent product (top level product)
        /// </summary>
        public string ProductId { get; protected set; }

        public string ProductName { get; protected set; }

        public string Path { get; protected set; }


        public ExpectedUpdateIntervalPolicy ExpectedUpdateIntervalPolicy
        {
            get => _expectedUpdateIntervalPolicy ?? ParentProduct?.ExpectedUpdateIntervalPolicy;
            set => _expectedUpdateIntervalPolicy = value;
        }

        public bool IsOwnExpectedUpdateInterval =>
            _expectedUpdateIntervalPolicy != null || ParentProduct?.ExpectedUpdateIntervalPolicy == null;


        internal void BuildProductNameAndPath()
        {
            var parentProduct = ParentProduct;
            if (parentProduct == null)
                return;

            var pathParts = new List<string>(1 << 2) { DisplayName };

            while (parentProduct.ParentProduct != null)
            {
                pathParts.Add(parentProduct.DisplayName);
                parentProduct = parentProduct.ParentProduct;
            }

            pathParts.Reverse();

            ProductId = parentProduct.Id;
            ProductName = parentProduct.DisplayName;
            Path = string.Join(CommonConstants.SensorPathSeparator, pathParts);
        }


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

        internal virtual void RemoveExpectedUpdateInterval()
        {
            ExpectedUpdateIntervalPolicy = null;
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
