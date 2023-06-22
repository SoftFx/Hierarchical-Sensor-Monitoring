using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies
{
    public sealed class ProductPolicyCollection : PolicyCollectionBase
    {
        private readonly Dictionary<SensorType, SensorPolicyCollection> _collectionsBySensorType = new();
        private readonly Dictionary<Type, SensorPolicyCollection> _collectionByPolicyType = new();

        private readonly List<Policy> _basePolicies = new(1 << 4);


        public SensorPolicyCollection this[SensorType type] => _collectionsBySensorType[type];


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
            if (_collectionByPolicyType.TryGetValue(typeof(T), out var collection))
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

            _collectionsBySensorType.Add(type, collection);
            _collectionByPolicyType.Add(typeof(PolicyType), collection);
        }
    }
}