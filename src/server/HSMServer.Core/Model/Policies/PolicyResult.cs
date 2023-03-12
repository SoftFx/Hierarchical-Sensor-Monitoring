using HSMServer.Core.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public readonly struct PolicyResult
    {
        internal static PolicyResult Ok { get; } = new();


        private HashSet<string> Comments { get; init; }

        private HashSet<string> Warnings { get; init; }

        private HashSet<string> Errors { get; init; }


        public SensorStatus Result
        {
            get
            {
                if (Comments.Count != 0)
                    return SensorStatus.OffTime;
                if (Errors.Count != 0)
                    return SensorStatus.Error;
                if (Warnings.Count != 0)
                    return SensorStatus.Warning;

                return SensorStatus.Ok;
            }
        }


        public string Message
        {
            get
            {
                var messageParts = new List<string>(3)
                {
                    JoinStrings(Comments),
                    JoinStrings(Warnings),
                    JoinStrings(Errors),
                };

                return JoinStrings(messageParts);
            }
        }


        public bool IsOk => Result == SensorStatus.Ok;

        public bool IsWarning => Warnings.Count > 0;

        public bool IsError => Errors.Count > 0;

        public bool IsOffTime => Comments.Count > 0;


        public PolicyResult()
        {
            Comments = new HashSet<string>();
            Warnings = new HashSet<string>();
            Errors = new HashSet<string>();
        }

        internal PolicyResult(SensorStatus result, string message) : this()
        {
            var targetHash = result switch
            {
                SensorStatus.Warning => Warnings,
                SensorStatus.Error => Errors,
                _ => Comments,
            };

            targetHash.Add(message);
        }


        internal static PolicyResult FromValue<T>(T value) where T : BaseValue
        {
            if (value.Status.IsOk())
                return Ok;

            var comment = value.Comment ?? $"User data has {value.Status} status";
            return new(value.Status, comment);
        }

        private static string JoinStrings(IEnumerable<string> items)
        {
            return string.Join(Environment.NewLine, items.Where(u => !string.IsNullOrEmpty(u)));
        }


        public static PolicyResult operator +(PolicyResult result1, PolicyResult result2)
        {
            return new()
            {
                Comments = result1.Comments.UnionFluent(result2.Comments),
                Warnings = result1.Warnings.UnionFluent(result2.Warnings),
                Errors = result1.Errors.UnionFluent(result2.Errors),
            };
        }

        public static PolicyResult operator -(PolicyResult result1, PolicyResult result2)
        {
            return new()
            {
                Comments = result1.Comments.ExceptFluent(result2.Comments),
                Warnings = result1.Warnings.ExceptFluent(result2.Warnings),
                Errors = result1.Errors.ExceptFluent(result2.Errors),
            };
        }

        public static bool operator ==(PolicyResult result1, PolicyResult result2) =>
            (result1.Result, result1.Message) == (result2.Result, result2.Message);

        public static bool operator !=(PolicyResult result1, PolicyResult result2)
            => !(result1 == result2);
    }
}
