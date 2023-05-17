using Client = HSMServer.Model.DataAlerts;
using Server = HSMServer.Core.Model.Policies;

namespace HSMServer.Extensions
{
    public static class OperationExtensions
    {
        internal static Client.Operation ToClient(this Server.PolicyOperation status) =>
            status switch
            {
                Server.PolicyOperation.LessThanOrEqual => Client.Operation.LessThanOrEqual,
                Server.PolicyOperation.LessThan => Client.Operation.LessThan,
                Server.PolicyOperation.GreaterThan => Client.Operation.GreaterThan,
                Server.PolicyOperation.GreaterThanOrEqual => Client.Operation.GreaterThanOrEqual,
                _ => Client.Operation.LessThanOrEqual,
            };

        internal static Server.PolicyOperation ToCore(this Client.Operation status) =>
            status switch
            {
                Client.Operation.LessThanOrEqual => Server.PolicyOperation.LessThanOrEqual,
                Client.Operation.LessThan => Server.PolicyOperation.LessThan,
                Client.Operation.GreaterThan => Server.PolicyOperation.GreaterThan,
                Client.Operation.GreaterThanOrEqual => Server.PolicyOperation.GreaterThanOrEqual,
                _ => Server.PolicyOperation.LessThanOrEqual,
            };
    }
}
