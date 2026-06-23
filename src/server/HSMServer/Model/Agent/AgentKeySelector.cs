using HSMCommon.Constants;
using HSMServer.Core.Model;
using System.Linq;

namespace HSMServer.Model.Agent
{
    /// <summary>
    /// Picks the access key baked into a product's agent bundle. The agent registers its default
    /// sensors at start and streams values, so it needs add-node/add-sensor + send-data permissions —
    /// the product DefaultKey has all of them and never expires. Pure (Core types only) so it is
    /// unit-tested without a web host.
    /// </summary>
    public static class AgentKeySelector
    {
        public const KeyPermissions AgentPermissions =
            KeyPermissions.CanSendSensorData | KeyPermissions.CanAddNodes | KeyPermissions.CanAddSensors;

        /// <summary>Returns the best agent key for the product, or null if it has none.</summary>
        public static AccessKeyModel Select(ProductModel product)
        {
            var keys = product.AccessKeys.Values;

            // Prefer the product's DefaultKey when it is valid for the agent's needs.
            foreach (var key in keys)
                if (key.DisplayName == CommonConstants.DefaultAccessKey && key.IsValid(AgentPermissions, out _))
                    return key;

            // Otherwise any valid key with the permissions the agent needs.
            var valid = keys.FirstOrDefault(k => k.IsValid(AgentPermissions, out _));
            if (valid is not null)
                return valid;

            // Last resort: the DefaultKey regardless of state, else any key (the admin can fix it up).
            return keys.FirstOrDefault(k => k.DisplayName == CommonConstants.DefaultAccessKey) ?? keys.FirstOrDefault();
        }
    }
}
