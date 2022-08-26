using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public readonly struct ValidationResult
    {
        internal static ValidationResult Ok { get; } = new();


        private HashSet<string> Messages { get; init; } = new();

        private HashSet<string> Warnings { get; init; } = new();

        private HashSet<string> Errors { get; init; } = new();


        public SensorStatus Result { get; init; } = SensorStatus.Ok;


        public string Message
        {
            get
            {
                var messageParts = new List<string>(3)
                {
                    JoinStrings(Messages),
                    JoinStrings(Warnings),
                    JoinStrings(Errors),
                };

                return JoinStrings(messageParts);
            }
        }


        public bool IsSuccess => Result == SensorStatus.Ok;

        public bool IsWarning => Warnings.Count > 0;

        public bool IsError => Errors.Count > 0;


        public ValidationResult()
        {
            Messages = new HashSet<string>();
            Warnings = new HashSet<string>();
            Errors = new HashSet<string>();
        }

        internal ValidationResult(string message, SensorStatus result) : this()
        {
            switch (result)
            {
                case SensorStatus.Warning:
                    Warnings.Add(message);
                    break;
                case SensorStatus.Error:
                    Errors.Add(message);
                    break;
                default:
                    Messages.Add(message);
                    break;
            }

            Result = result;
        }


        private static string JoinStrings(IEnumerable<string> items)
        {
            return string.Join(Environment.NewLine, items.Where(u => !string.IsNullOrEmpty(u)));
        }

        public static ValidationResult operator +(ValidationResult result1, ValidationResult result2)
        {
            static HashSet<string> GetUnionErrors(HashSet<string> errors1, HashSet<string> errors2)
            {
                var errors = new HashSet<string>(errors1);
                errors.UnionWith(errors2);

                return errors;
            }

            return new()
            {
                Result = result1.Result > result2.Result ? result1.Result : result2.Result,

                Messages = GetUnionErrors(result1.Messages, result2.Messages),
                Warnings = GetUnionErrors(result1.Warnings, result2.Warnings),
                Errors = GetUnionErrors(result1.Errors, result2.Errors),
            };
        }

        public static ValidationResult operator -(ValidationResult result1, ValidationResult result2)
        {
            static HashSet<string> GetExceptErrors(HashSet<string> errors1, HashSet<string> errors2)
            {
                var errors = new HashSet<string>(errors1);
                errors.ExceptWith(errors2);

                return errors;
            }

            var warnings = GetExceptErrors(result1.Warnings, result2.Warnings);
            var errors = GetExceptErrors(result1.Errors, result2.Errors);
            var messages = GetExceptErrors(result1.Messages, result2.Messages);

            SensorStatus result = SensorStatus.Ok;
            if (messages.Count != 0)
                result = SensorStatus.Unknown;
            else if (errors.Count != 0)
                result = SensorStatus.Error;
            else if (warnings.Count != 0)
                result = SensorStatus.Warning;

            return new()
            {
                Result = result,
                Warnings = warnings,
                Errors = errors,
                Messages = messages,
            };
        }

        public static bool operator ==(ValidationResult result1, ValidationResult result2) =>
            result1.Message == result2.Message && result1.Result == result2.Result;

        public static bool operator !=(ValidationResult result1, ValidationResult result2)
            => !(result1 == result2);
    }
}
