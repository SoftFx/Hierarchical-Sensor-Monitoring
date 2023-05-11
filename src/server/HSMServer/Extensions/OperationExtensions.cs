using Client = HSMServer.Model.DataAlerts;
using Server = HSMServer.Core.Model.Policies;

namespace HSMServer.Extensions
{
    public static class OperationExtensions
    {
        internal static Client.Operation ToClient(this Server.Operation status) =>
            status switch
            {
                Server.Operation.LessThanOrEqual => Client.Operation.LessThanOrEqual,
                Server.Operation.LessThan => Client.Operation.LessThan,
                Server.Operation.GreaterThan => Client.Operation.GreaterThan,
                Server.Operation.GreaterThanOrEqual => Client.Operation.GreaterThanOrEqual,
                _ => Client.Operation.LessThanOrEqual,
            };

        internal static Server.Operation ToCore(this Client.Operation status) =>
            status switch
            {
                Client.Operation.LessThanOrEqual => Server.Operation.LessThanOrEqual,
                Client.Operation.LessThan => Server.Operation.LessThan,
                Client.Operation.GreaterThan => Server.Operation.GreaterThan,
                Client.Operation.GreaterThanOrEqual => Server.Operation.GreaterThanOrEqual,
                _ => Server.Operation.LessThanOrEqual,
            };
    }
}
