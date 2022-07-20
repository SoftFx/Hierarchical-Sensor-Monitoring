using HSMServer.Core.Model;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.SensorsDataValidation
{
    public readonly struct ValidationResult
    {
        private readonly HashSet<string> _warnings;
        private readonly HashSet<string> _errors;


        internal static ValidationResult Success { get; } = new();


        public SensorStatus Result { get; }


        public string Warning => string.Join(Environment.NewLine, _warnings);

        public string Error => string.Join(Environment.NewLine, _errors);

        public string Message
        {
            get
            {
                var messageParts = new List<string>(2);

                if (_warnings.Count > 0)
                    messageParts.Add(Warning);
                if (_errors.Count > 0)
                    messageParts.Add(Error);

                return string.Join(Environment.NewLine, messageParts);
            }
        }


        public bool IsSuccess => Result == SensorStatus.Ok;

        public bool IsWarning => Result == SensorStatus.Warning;

        public bool IsError => Result == SensorStatus.Error;


        public ValidationResult()
        {
            _warnings = new HashSet<string>();
            _errors = new HashSet<string>();

            Result = SensorStatus.Ok;
        }

        internal ValidationResult(string messagge, SensorStatus result) : this()
        {
            switch (result)
            {
                case SensorStatus.Warning:
                    _warnings.Add(messagge);
                    break;
                case SensorStatus.Error:
                    _errors.Add(messagge);
                    break;
            }

            Result = result;
        }

        private ValidationResult(HashSet<string> warnings, HashSet<string> errors) : this()
        {
            _warnings = warnings;
            _errors = errors;

            if (_errors.Count > 0)
                Result = SensorStatus.Error;
            else if (_warnings.Count > 0)
                Result = SensorStatus.Warning;
        }


        public static ValidationResult operator +(ValidationResult result1, ValidationResult result2)
        {
            HashSet<string> GetUnionErrors(HashSet<string> errors1, HashSet<string> errors2)
            {
                var errors = new HashSet<string>(errors1);
                errors.UnionWith(errors2);

                return errors;
            }

            return new(GetUnionErrors(result1._warnings, result2._warnings), GetUnionErrors(result1._errors, result2._errors));
        }
    }
}
