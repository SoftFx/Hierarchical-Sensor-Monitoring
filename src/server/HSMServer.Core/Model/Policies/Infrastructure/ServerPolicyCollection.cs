using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies
{
    public sealed class ServerPolicyCollection : IEnumerable<CollectionProperty>
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


        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerator<CollectionProperty> GetEnumerator() => _properties.Values.GetEnumerator();

        public IEnumerable<Guid> GetIds() => _properties.Values.Where(p => p.IsSet)
                                                               .Select(p => p.PolicyGuid);


        private CollectionProperty<T> Register<T>() where T : ServerPolicy, new()
        {
            var property = new CollectionProperty<T>();

            _properties[typeof(T)] = property;

            return property;
        }
    }
}
