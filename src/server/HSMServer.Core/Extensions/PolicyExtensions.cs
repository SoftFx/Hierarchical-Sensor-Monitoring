using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Extensions
{
    public static class PolicyExtensions
    {
        public static bool IsStatusChangeResult(this List<PolicyCondition> conditions)
        {
            if (conditions.Count != 1)
                return false;

            var condition = conditions[0];

            return condition.Property is PolicyProperty.Status && condition.Operation is PolicyOperation.IsChanged;
        }


        public static TimeSpan ToTime(this AlertRepeatMode mode) => mode switch
        {
            AlertRepeatMode.FiveMinutes => TimeSpan.FromMinutes(5),
            AlertRepeatMode.TenMinutes => TimeSpan.FromMinutes(10),
            AlertRepeatMode.FifteenMinutes => TimeSpan.FromMinutes(15),
            AlertRepeatMode.ThirtyMinutes => TimeSpan.FromMinutes(30),
            AlertRepeatMode.Hourly => TimeSpan.FromHours(1),
            AlertRepeatMode.Daily => TimeSpan.FromDays(1),
            AlertRepeatMode.Weekly => TimeSpan.FromDays(7),
            _ => TimeSpan.Zero,
        };
    }
}