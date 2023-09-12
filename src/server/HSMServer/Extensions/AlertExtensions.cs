using HSMServer.Core.Model.Policies;
using HSMServer.Model.DataAlerts;
using System;

namespace HSMServer.Extensions
{
    public static class AlertExtensions
    {
        public static string ToVisibility(this bool isVisible) => isVisible ? "d-flex" : "d-none";

        public static AlertProperty ToClient(this PolicyProperty property) =>
            property switch
            {
                PolicyProperty.Status => AlertProperty.Status,
                PolicyProperty.Comment => AlertProperty.Comment,
                PolicyProperty.Value => AlertProperty.Value,
                PolicyProperty.Min => AlertProperty.Min,
                PolicyProperty.Max => AlertProperty.Max,
                PolicyProperty.Mean => AlertProperty.Mean,
                PolicyProperty.Count => AlertProperty.Count,
                PolicyProperty.LastValue => AlertProperty.LastValue,
                PolicyProperty.OriginalSize => AlertProperty.OriginalSize,
                PolicyProperty.NewSensorData => AlertProperty.NewSensorData,
                _ => throw new NotImplementedException()
            };

        public static PolicyProperty ToCore(this AlertProperty property) =>
            property switch
            {
                AlertProperty.Status => PolicyProperty.Status,
                AlertProperty.Comment => PolicyProperty.Comment,
                AlertProperty.Value => PolicyProperty.Value,
                AlertProperty.Min => PolicyProperty.Min,
                AlertProperty.Max => PolicyProperty.Max,
                AlertProperty.Mean => PolicyProperty.Mean,
                AlertProperty.Count => PolicyProperty.Count,
                AlertProperty.LastValue => PolicyProperty.LastValue,
                AlertProperty.OriginalSize => PolicyProperty.OriginalSize,
                AlertProperty.NewSensorData => PolicyProperty.NewSensorData,
                _ => throw new NotImplementedException()
            };
    }
}
