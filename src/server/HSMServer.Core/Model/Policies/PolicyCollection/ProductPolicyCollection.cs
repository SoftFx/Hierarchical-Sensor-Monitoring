using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies
{
    public sealed class ProductPolicyCollection : PolicyCollectionBase
    {
        private readonly Dictionary<SensorType, SensorPolicyCollection> _bySensorType = new();
        private readonly Dictionary<Type, SensorPolicyCollection> _byPolicyType = new();

        private readonly List<Policy> _basePolicies = new(1 << 4);


        internal SensorPolicyCollection this[SensorType type] => _bySensorType[type];

        internal override IEnumerable<Guid> Ids => _basePolicies.Select(u => u.Id);


        internal ProductPolicyCollection()
        {
            Register<IntegerValue, IntegerPolicy>(SensorType.Integer);
            Register<DoubleValue, DoublePolicy>(SensorType.Double);

            Register<BooleanValue, BooleanPolicy>(SensorType.Boolean);
            Register<StringValue, StringPolicy>(SensorType.String);
            Register<FileValue, FilePolicy>(SensorType.File);

            Register<IntegerBarValue, IntegerBarPolicy>(SensorType.IntegerBar);
            Register<DoubleBarValue, DoubleBarPolicy>(SensorType.DoubleBar);

            Register<VersionValue, VersionPolicy>(SensorType.Version);
            Register<TimeSpanValue, TimeSpanPolicy>(SensorType.TimeSpan);
        }


        internal override void AddPolicy<T>(T policy)
        {
            if (_byPolicyType.TryGetValue(typeof(T), out var collection))
            {
                collection.AddPolicy(policy);

                _basePolicies.Add(policy);
            }
        }


        public override IEnumerator<Policy> GetEnumerator() => _basePolicies.GetEnumerator();


        private void Register<ValueType, PolicyType>(SensorType type)
            where ValueType : BaseValue
            where PolicyType : Policy<ValueType>, new()
        {
            var collection = new SensorPolicyCollection<ValueType, PolicyType>();

            _bySensorType.Add(type, collection);
            _byPolicyType.Add(typeof(PolicyType), collection);
        }

        internal override void ApplyPolicies(List<string> policyIds, Dictionary<string, PolicyEntity> allPolicies)
        {
            throw new NotImplementedException();
        }
    }
}