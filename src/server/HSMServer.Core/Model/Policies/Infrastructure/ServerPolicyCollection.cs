using HSMServer.Core.Model.Policies.Infrastructure;
using HSMServer.Core.Model.Policies.ServerPolicies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies
{
    public sealed class ServerPolicyCollection : PolicyCollectionBase<CollectionProperty>
    {
        private readonly Dictionary<Type, CollectionProperty> _properties = new();


        internal override IEnumerable<Guid> Ids => _properties.Values.Where(p => p.IsSet).Select(p => p.PolicyGuid);


        public CollectionProperty<ExpectedUpdateIntervalPolicy> ExpectedUpdate { get; }

        public CollectionProperty<SavedIntervalPolicy> SavedHistoryPeriod { get; }

        public CollectionProperty<SelfDestroyPolicy> SelfDestroy { get; }



        public CollectionProperty<RestoreOffTimePolicy> RestoreOffTime { get; }

        public CollectionProperty<RestoreWarningPolicy> RestoreWarning { get; }

        public CollectionProperty<RestoreErrorPolicy> RestoreError { get; }


        internal ServerPolicyCollection()
        {
            ExpectedUpdate = Register<ExpectedUpdateIntervalPolicy>();
            SavedHistoryPeriod = Register<SavedIntervalPolicy>();
            SelfDestroy = Register<SelfDestroyPolicy>();

            RestoreOffTime = Register<RestoreOffTimePolicy>();
            RestoreWarning = Register<RestoreWarningPolicy>();
            RestoreError = Register<RestoreErrorPolicy>();
        }


        internal void ApplyPolicy<T>(T serverPolicy) where T : Policy
        {
            if (_properties.TryGetValue(serverPolicy.GetType(), out var property))
                property.SetPolicy(serverPolicy);
        }

        internal void ApplyParentPolicies(ServerPolicyCollection parentCollection)
        {
            foreach (var (policyType, property) in _properties)
                property.ParentProperty = parentCollection._properties[policyType];
        }


        internal PolicyResult CheckRestorePolicies(SensorStatus status, DateTime lastUpdate)
        {
            var result = PolicyResult.Ok;

            //result += RestoreOffTime.Policy.Validate(status, lastUpdate); //TODO uncomment after separate configucation
            result += RestoreWarning.Policy.Validate(status, lastUpdate);
            result += RestoreError.Policy.Validate(status, lastUpdate);

            return result;
        }

        internal bool HasUpdateTimeout(DateTime? receivingTime)
        {
            var oldResult = Result;

            Result -= ExpectedUpdate.Policy.Fail;

            if (!receivingTime.HasValue)
                return false;

            Result += ExpectedUpdate.Policy.Validate(receivingTime.Value);

            return Result != oldResult;
        }


        public override IEnumerator<CollectionProperty> GetEnumerator() => _properties.Values.GetEnumerator();


        private CollectionProperty<T> Register<T>() where T : ServerPolicy, new()
        {
            var property = new CollectionProperty<T>();

            _properties[typeof(T)] = property;

            return property;
        }
    }
}
