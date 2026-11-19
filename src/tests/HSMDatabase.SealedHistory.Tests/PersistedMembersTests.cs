using System.Reflection;
using HSMCommon.Model;
using HSMDatabase.SealedHistory;
using HSMDatabase.SealedHistory.Format;
using MemoryPack;
using Xunit;

namespace HSMDatabase.SealedHistory.Tests
{
    /// <summary>
    /// Guards against a value type gaining a persisted field the codec drops. Every public settable member of
    /// every sealed value record is set to a sample value; when MemoryPack's bytes change, the member is
    /// persisted and the chunk must bring the sample back. A member MemoryPack persists without a public setter
    /// fails the test, because it cannot be checked this way. The same members must be compared by
    /// <see cref="RowFidelity"/>, the oracle the sealing job verifies with.
    /// </summary>
    public class PersistedMembersTests
    {
        private static readonly Type[] SealedRecords =
        [
            typeof(BooleanValue), typeof(IntegerValue), typeof(DoubleValue), typeof(StringValue), typeof(IntegerBarValue),
            typeof(DoubleBarValue), typeof(TimeSpanValue), typeof(VersionValue), typeof(RateValue), typeof(EnumValue),
        ];

        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly DateTime Time = Samples.Start.AddHours(5);


        public static IEnumerable<object[]> Records => SealedRecords.Select(t => new object[] { t });


        [Fact]
        public void Every_sealed_type_is_checked()
        {
            Assert.Equal(Samples.SealedTypes.OrderBy(t => t), SealedRecords.Select(t => ((BaseValue)Activator.CreateInstance(t)).Type).OrderBy(t => t));
        }

        [Theory]
        [MemberData(nameof(Records))]
        public void Every_persisted_member_round_trips(Type record)
        {
            var persisted = new List<string>();

            foreach (var member in SettableMembers(record))
            {
                var baseline = Baseline(record);
                var changed = Baseline(record);

                if (!TrySample(member, out var sample))
                {
                    if (IsPersistedByRule(member))
                        Assert.Fail($"{record.Name}.{member.Name} ({MemberType(member).Name}) has no sample value: extend the codec and this test");
                    continue;
                }

                SetValue(changed, member, sample);

                bool persistedByMemoryPack = !MemoryPackSerializer.Serialize<BaseValue>(baseline).AsSpan()
                    .SequenceEqual(MemoryPackSerializer.Serialize<BaseValue>(changed));

                Assert.Equal(IsPersistedByRule(member), persistedByMemoryPack);

                if (!persistedByMemoryPack)
                    continue;

                persisted.Add(member.Name);

                Assert.True(RowFidelity.FindDifference(new HistoryRow(Time.Ticks, changed), new HistoryRow(Time.Ticks, baseline)) is not null,
                    $"{record.Name}.{member.Name}: RowFidelity does not compare this member");

                var rows = new List<HistoryRow> { new(Time.Ticks, changed) };
                var decoded = Fidelity.RoundTrip(rows).Single().Value;

                Assert.True(SameValue(GetValue(decoded, member), sample), $"{record.Name}.{member.Name}: the chunk lost the value {sample}");
            }

            // The rule-based enumeration must find the members MemoryPack writes, with no public setter missing.
            foreach (var member in RuleMembers(record))
                Assert.True(persisted.Contains(member.Name), $"{record.Name}.{member.Name} is persisted by MemoryPack but was not checked");

            Assert.Contains(nameof(BaseValue.Time), persisted);
            Assert.Contains(nameof(BaseValue.ReceivingTime), persisted);
        }


        private static BaseValue Baseline(Type record)
        {
            var value = (BaseValue)Activator.CreateInstance(record);
            value.Time = Time;
            return value with { ReceivingTime = Time };
        }

        private static IEnumerable<MemberInfo> SettableMembers(Type record)
        {
            foreach (var p in record.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                if (p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
                    yield return p;

            foreach (var f in record.GetFields(BindingFlags.Instance | BindingFlags.Public))
                if (!f.IsInitOnly)
                    yield return f;
        }

        /// <summary>MemoryPack's member rule for a default (object) layout: public readable members with a setter,
        /// public fields, and anything marked [MemoryPackInclude]; [MemoryPackIgnore] wins.</summary>
        private static IEnumerable<MemberInfo> RuleMembers(Type record)
        {
            var seen = new HashSet<string>();

            for (var t = record; t is not null && t != typeof(object); t = t.BaseType)
            {
                foreach (var p in t.GetProperties(Instance | BindingFlags.DeclaredOnly))
                    if (IsPersistedByRule(p) && seen.Add(p.Name))
                        yield return p;

                foreach (var f in t.GetFields(Instance | BindingFlags.DeclaredOnly))
                    if (IsPersistedByRule(f) && seen.Add(f.Name))
                        yield return f;
            }
        }

        private static bool IsPersistedByRule(MemberInfo member)
        {
            if (member.IsDefined(typeof(MemoryPackIgnoreAttribute), true))
                return false;
            if (member.IsDefined(typeof(MemoryPackIncludeAttribute), true))
                return true;
            if (member.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), true))
                return false;

            return member switch
            {
                PropertyInfo p => p.GetMethod is { IsPublic: true } && p.SetMethod is not null && p.GetIndexParameters().Length == 0,
                FieldInfo f => f.IsPublic,
                _ => false,
            };
        }

        private static bool TrySample(MemberInfo member, out object sample)
        {
            var type = MemberType(member);

            sample = member.Name switch
            {
                nameof(BaseValue.Time) => Time.AddDays(1),
                nameof(BaseValue.ReceivingTime) => RawBits.ToDateTime(Time.Ticks + 7 * TimeSpan.TicksPerMillisecond, 2),
                nameof(BaseValue.AggregatedValuesCount) => 987_654_321_012L,
                _ => null,
            };

            if (sample is not null)
                return true;

            var underlying = Nullable.GetUnderlyingType(type) ?? type;

            sample = underlying switch
            {
                _ when underlying == typeof(int) => 123_456_789,
                _ when underlying == typeof(long) => 987_654_321_012L,
                _ when underlying == typeof(double) => 3.14159,
                _ when underlying == typeof(bool) => true,
                _ when underlying == typeof(string) => "sample é",
                _ when underlying == typeof(DateTime) => RawBits.ToDateTime(Time.Ticks + 12_345, 2),
                _ when underlying == typeof(TimeSpan) => TimeSpan.FromTicks(123_457),
                _ when underlying == typeof(Version) => new Version(1, 2, 3, 4),
                _ when underlying == typeof(SensorStatus) => SensorStatus.Error,
                _ => null,
            };

            return sample is not null;
        }

        private static Type MemberType(MemberInfo member) => member is PropertyInfo p ? p.PropertyType : ((FieldInfo)member).FieldType;

        private static object GetValue(object target, MemberInfo member) =>
            member is PropertyInfo p ? p.GetValue(target) : ((FieldInfo)member).GetValue(target);

        private static void SetValue(object target, MemberInfo member, object value)
        {
            if (member is PropertyInfo p)
                p.SetValue(target, value);
            else
                ((FieldInfo)member).SetValue(target, value);
        }

        private static bool SameValue(object actual, object expected) => (actual, expected) switch
        {
            (double a, double e) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(e),
            (DateTime a, DateTime e) => a.Ticks == e.Ticks && RawBits.KindBits(a) == RawBits.KindBits(e),
            _ => Equals(actual, expected),
        };
    }
}
