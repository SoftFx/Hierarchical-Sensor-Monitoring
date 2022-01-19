using HSMSensorDataObjects.FullDataObject;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.SensorsDataValidation
{
    public enum ResultType
    {
        Unknown = 0,
        Ok,
        Warning,
        Error,
    }


    public readonly struct ValidationResult
    {
        private readonly HashSet<string> _warnings;
        private readonly HashSet<string> _errors;


        public ResultType ResultType { get; }

        public SensorValueBase Data { get; }


        public string Warning => string.Join(Environment.NewLine, _warnings);

        public string Error => string.Join(Environment.NewLine, _errors);


        public bool IsSuccess => ResultType == ResultType.Ok;

        public bool IsWarning => ResultType == ResultType.Warning;

        public bool IsError => ResultType == ResultType.Error;


        public ValidationResult(SensorValueBase data) : this()
        {
            _warnings = new HashSet<string>();
            _errors = new HashSet<string>();

            Data = data;

            ResultType = ResultType.Ok;
        }

        public ValidationResult(SensorValueBase data, string warning) : this(data)
        {
            _warnings.Add(warning);
            ResultType = ResultType.Warning;
        }

        public ValidationResult(string error) : this(data: null)
        {
            _errors.Add(error);
            ResultType = ResultType.Error;
        }

        private ValidationResult(SensorValueBase data, HashSet<string> warnings, HashSet<string> errors) : this(data)
        {
            _warnings = warnings;
            _errors = errors;

            if (_errors.Count > 0)
                ResultType = ResultType.Error;
            else if (_warnings.Count > 0)
                ResultType = ResultType.Warning;
        }


        public static ValidationResult operator +(ValidationResult result1, ValidationResult result2)
        {
            HashSet<string> GetUnionErrors(HashSet<string> errors1, HashSet<string> errors2)
            {
                var errors = new HashSet<string>(errors1);
                errors.UnionWith(errors2);

                return errors;
            }

            var worstResultData = result1.ResultType >= result2.ResultType ? result1.Data : result2.Data;

            return new(worstResultData, GetUnionErrors(result1._warnings, result2._warnings), GetUnionErrors(result1._errors, result2._errors));
        }
    }
}
