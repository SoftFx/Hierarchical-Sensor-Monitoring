using HSMServer.Core.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public readonly struct SensorResult : IEquatable<SensorResult>
    {
        private readonly SortedSet<(SensorStatus status, string comment)> _results;


        internal static SensorResult Ok { get; } = new(SensorStatus.Ok, null);

        internal (SensorStatus, string) ToTuple => _results.Count > 0 ? _results.Max : Ok.ToTuple;


        public SensorStatus Status => _results.Count > 0 ? _results.Max.status : SensorStatus.Ok;

        public string Message => string.Join(Environment.NewLine, _results.Select(u => u.comment)
                                                                          .Where(u => !string.IsNullOrEmpty(u)));


        public bool HasOffTime => Status >= SensorStatus.OffTime;

        public bool HasWarning => Status >= SensorStatus.Warning;

        public bool HasError => Status >= SensorStatus.Error;


        public bool IsOk => Status.IsOk();


        private SensorResult(SortedSet<(SensorStatus, string)> result)
        {
            _results = result;
        }

        internal SensorResult(BaseValue value) : this(value.Status, value.Comment) { }

        internal SensorResult(SensorStatus result, string comment)
        {
            _results = new() { (result, comment) };
        }


        public static bool operator ==(SensorResult first, SensorResult second) => first.Equals(second);

        public static bool operator !=(SensorResult first, SensorResult second) => !first.Equals(second);

        public static SensorResult operator +(SensorResult first, SensorResult second) =>
            new(first._results.UnionFluent(second._results));


        public override bool Equals(object obj) => obj is SensorResult second && ToTuple == second.ToTuple;

        public override int GetHashCode() => ToTuple.GetHashCode();

        bool IEquatable<SensorResult>.Equals(SensorResult other) => Equals(other);
    }
}
