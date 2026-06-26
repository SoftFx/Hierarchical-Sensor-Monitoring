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

        /// <summary>
        /// Returns the best agent key for the product, or null when none actually satisfies
        /// <see cref="AgentPermissions"/>. We never hand back an expired/under-permissioned key as a
        /// "last resort": it would bake a credential that fails to authenticate at runtime into the
        /// bundle with no signal to the admin. Null instead lets the caller refuse the download
        /// (BadRequest) so the admin fixes the key first.
        /// </summary>
        public static AccessKeyModel Select(ProductModel product)
        {
            // Order by Id so the choice is deterministic if a product happens to have more than one key
            // sharing a name (the dictionary's own enumeration order is not guaranteed).
            var keys = product.AccessKeys.Values.OrderBy(k => k.Id).ToList();

            // Prefer the product's DefaultKey when it is valid for the agent's needs.
            foreach (var key in keys)
                if (key.DisplayName == CommonConstants.DefaultAccessKey && key.IsValid(AgentPermissions, out _))
                    return key;

            // Otherwise any valid key with the permissions the agent needs, or null if there is none.
            return keys.FirstOrDefault(k => k.IsValid(AgentPermissions, out _));
        }
    }
}
