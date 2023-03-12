using System;
using System.Collections;
using System.Collections.Generic;

namespace HSMServer.Core.Model.Policies
{
    public sealed class ServerPolicyCollection : IEnumerable<Guid>
    {
        private readonly Dictionary<Type, CollectionProperty> _properties = new();


        public CollectionProperty<ExpectedUpdateIntervalPolicy> ExpectedUpdate { get; }


        public CollectionProperty<RestoreOffTimePolicy> RestoreOffTimeStatus { get; }

        public CollectionProperty<RestoreWarningPolicy> RestoreWarningStatus { get; }

        public CollectionProperty<RestoreErrorPolicy> RestoreErrorStatus { get; }


        internal ServerPolicyCollection()
        {
            ExpectedUpdate = Register<ExpectedUpdateIntervalPolicy>();

            RestoreOffTimeStatus = Register<RestoreOffTimePolicy>();
            RestoreWarningStatus = Register<RestoreWarningPolicy>();
            RestoreErrorStatus = Register<RestoreErrorPolicy>();
        }


        internal void ApplyPolicy<T>(T serverPolicy) where T : Policy
        {
            if (_properties.TryGetValue(typeof(T), out var property))
                property.SetPolicy(serverPolicy);
        }

        internal void ApplyParentPolicies(ServerPolicyCollection parentCollection)
        {
            foreach (var (policyType, property) in _properties)
                property.ParentProperty = parentCollection._properties[policyType];
        }

        internal PolicyResult CheckRestorePolicies(DateTime date)
        {
            var result = PolicyResult.Ok;

            result += RestoreOffTimeStatus.Policy?.Validate(date) ?? PolicyResult.Ok;
            result += RestoreWarningStatus.Policy?.Validate(date) ?? PolicyResult.Ok;
            result += RestoreErrorStatus.Policy?.Validate(date) ?? PolicyResult.Ok;

            return result;
        }

        public IEnumerator<Guid> GetEnumerator()
        {
            foreach (var property in _properties.Values)
                if (property.IsSet)
                    yield return property.PolicyGuid;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();


        private CollectionProperty<T> Register<T>() where T : ServerPolicy
        {
            var property = new CollectionProperty<T>();

            _properties[typeof(T)] = property;

            return property;
        }
    }
}
