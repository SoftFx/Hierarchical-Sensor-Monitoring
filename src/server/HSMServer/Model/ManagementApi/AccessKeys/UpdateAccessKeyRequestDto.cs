using System.ComponentModel;

namespace HSMServer.Model.ManagementApi.AccessKeys
{
    /// <summary>
    /// The update-access-key request body — a partial update: every field is
    /// optional and null means "leave unchanged" (the PolicyDto "null means
    /// omit" convention).
    /// </summary>
    public sealed class UpdateAccessKeyRequestDto
    {
        /// <summary>The key's new display name (same rules as on create: 1..100 characters, no control/bidi-override characters, unique among the product's keys). Null = leave unchanged.</summary>
        [Description("The key's new display name (same rules as on create: 1..100 characters, no control/bidi-override characters, unique among the product's keys). Null = leave unchanged.")]
        public string DisplayName { get; init; }

        /// <summary>The key's new permission names (at least one). Null = leave unchanged.</summary>
        [Description("The key's new permission names (at least one). Null = leave unchanged.")]
        public string[] Permissions { get; init; }

        /// <summary>The key's new stored state: 'active' (unblock) or 'blocked' (the kill-switch for a misbehaving agent; the agent's data keeps flowing, its key is refused). 'expired' is derived, not settable. Null = leave unchanged.</summary>
        [Description("The key's new stored state: 'active' (unblock) or 'blocked' (the kill-switch for a misbehaving agent; the agent's data keeps flowing, its key is refused). 'expired' is derived, not settable. Null = leave unchanged.")]
        public string State { get; init; }
    }
}
