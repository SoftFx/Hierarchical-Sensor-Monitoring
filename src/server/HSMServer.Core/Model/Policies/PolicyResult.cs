using HSMServer.Core.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public readonly struct PolicyResult : IEquatable<PolicyResult>
    {
        private readonly SortedSet<(SensorStatus status, string icon, string comment)> _results;


        internal static PolicyResult Ok { get; } = new(SensorStatus.Ok, string.Empty);

        internal (SensorStatus, string) ToTuple => (Status, Message);


        public SensorStatus Status => _results.Count > 0 ? _results.Max.status : SensorStatus.Ok;

        public string Icon => _results.Count > 0 ? _results.Max.icon : Status.ToIcon();

        public string Message => string.Join(Environment.NewLine, _results.Select(u => u.comment)
                                                                          .Where(u => !string.IsNullOrEmpty(u)));


        public bool HasOffTime => Status >= SensorStatus.OffTime;

        public bool HasWarning => Status >= SensorStatus.Warning;

        public bool HasError => Status >= SensorStatus.Error;


        public bool IsOk => Status.IsOk();


        private PolicyResult(SortedSet<(SensorStatus, string, string)> result)
        {
            _results = result;
        }

        internal PolicyResult(SensorStatus result, string comment, string icon = null)
        {
            _results = new() { (result, icon, comment) };
        }


        internal static PolicyResult FromValue<T>(T value) where T : BaseValue
        {
            if (value.Status.IsOk())
                return Ok;

            var comment = value.Comment ?? $"User data has {value.Status} status";
            return new(value.Status, comment);
        }


        public static bool operator ==(PolicyResult first, PolicyResult second) => first.Equals(second);

        public static bool operator !=(PolicyResult first, PolicyResult second) => !first.Equals(second);

        public static PolicyResult operator +(PolicyResult first, PolicyResult second) =>
            new(first._results.UnionFluent(second._results));

        public static PolicyResult operator -(PolicyResult first, PolicyResult second) =>
            new(first._results.ExceptFluent(second._results));


        public override bool Equals(object obj) => obj is PolicyResult second && ToTuple == second.ToTuple;

        public override int GetHashCode() => ToTuple.GetHashCode();

        bool IEquatable<PolicyResult>.Equals(PolicyResult other) => Equals(other);
    }
}
