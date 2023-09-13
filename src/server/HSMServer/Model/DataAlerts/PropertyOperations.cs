using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public static class PropertyOperations
    {
        private static readonly List<PolicyOperation> _status = new()
        {
            PolicyOperation.IsChanged,
            PolicyOperation.IsChangedToOk,
            PolicyOperation.IsChangedToError,
            PolicyOperation.IsOk,
            PolicyOperation.IsError
        };

        private static readonly List<PolicyOperation> _comment = new()
        {
            PolicyOperation.Equal,
            PolicyOperation.NotEqual,
            PolicyOperation.Contains,
            PolicyOperation.StartsWith,
            PolicyOperation.EndsWith,
            PolicyOperation.IsChanged,
        };

        private static readonly List<PolicyOperation> _string = new()
        {
            PolicyOperation.Equal,
            PolicyOperation.NotEqual,
            PolicyOperation.Contains,
            PolicyOperation.StartsWith,
            PolicyOperation.EndsWith,
        };

        private static readonly List<PolicyOperation> _numeric = new()
        {
            PolicyOperation.LessThanOrEqual,
            PolicyOperation.LessThan,
            PolicyOperation.GreaterThan,
            PolicyOperation.GreaterThanOrEqual,
            PolicyOperation.NotEqual,
            PolicyOperation.Equal,
        };


        public static List<PolicyOperation> GetOperations(this ConditionViewModel condition) =>
            condition.GetOperations(condition.Property);

        public static List<PolicyOperation> GetOperations(this ConditionViewModel condition, string propertyStr)
        {
            if (Enum.TryParse<AlertProperty>(propertyStr, out var property))
                return condition.GetOperations(property);

            throw new ArgumentException("Incorrect property");
        }

        private static List<PolicyOperation> GetOperations(this ConditionViewModel condition, AlertProperty property) =>
            property switch
            {
                AlertProperty.Status => _status,
                AlertProperty.Comment => _comment,
                AlertProperty.Value => condition is StringConditionViewModel ? _string : _numeric,
                AlertProperty.Min or AlertProperty.Max or AlertProperty.Mean or AlertProperty.Count or AlertProperty.LastValue
                                  or AlertProperty.Length or AlertProperty.OriginalSize => _numeric,
                AlertProperty.NewSensorData or AlertProperty.Sensitivity or AlertProperty.TimeToLive => new(),
                _ => throw new NotSupportedException(),
            };
    }
}
