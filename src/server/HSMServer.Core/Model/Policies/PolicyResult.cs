using HSMServer.Core.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public readonly struct PolicyResult
    {
        private readonly SortedSet<(SensorStatus status, string comment)> _results;


        internal static PolicyResult Ok { get; } = new(SensorStatus.Ok, string.Empty);


        public SensorStatus Status => _results.Count > 0 ? _results.Max.status : SensorStatus.Ok;

        public string Message => string.Join(Environment.NewLine, _results.Select(u => u.comment).Where(u => !string.IsNullOrEmpty(u)));


        public bool HasOffTime => Status >= SensorStatus.OffTime;

        public bool HasWarning => Status >= SensorStatus.Warning;

        public bool HasError => Status >= SensorStatus.Error;


        public bool IsOk => Status.IsOk();


        private PolicyResult(SortedSet<(SensorStatus, string)> result)
        {
            _results = result;
        }

        internal PolicyResult(SensorStatus result, string comment)
        {
            _results = new() { (result, comment) };
        }


        internal static PolicyResult FromValue<T>(T value) where T : BaseValue
        {
            if (value.Status.IsOk())
                return Ok;

            var comment = value.Comment ?? $"User data has {value.Status} status";
            return new(value.Status, comment);
        }


        public void Deconstruct(out SensorStatus status, out string message)
        {
            status = Status;
            message = Message;
        }


        public static PolicyResult operator +(PolicyResult first, PolicyResult second)
        {
            return new(first._results.UnionFluent(second._results));
        }

        public static PolicyResult operator -(PolicyResult first, PolicyResult second)
        {
            return new(first._results.ExceptFluent(second._results));
        }

        public static bool operator ==(PolicyResult first, PolicyResult second) =>
            (first.Status, first.Message) == (second.Status, second.Message);

        public static bool operator !=(PolicyResult result1, PolicyResult result2)
            => !(result1 == result2);
    }
}
