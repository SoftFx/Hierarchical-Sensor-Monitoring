using System;
using System.ComponentModel;

namespace HSMServer.Model.ManagementApi.AccessKeys
{
    /// <summary>
    /// A product access key — the credential an HSM agent sends in the Key
    /// header of every sensor-data request. This is the id-discovery surface
    /// for the agent-installation scenario (#1787): the listed ids are the
    /// handles the write surface (#1789) takes, and lastUseTime/lastIp answer
    /// "has the installed agent connected" without any new endpoint.
    /// </summary>
    // The key's GUID IS the credential itself (there is no separate secret),
    // and it is returned DELIBERATELY: the web UI renders it to every user
    // who can see the product's keys, and a caller at this sight level can
    // already act as the key's owner (the same mirror-model reasoning as the
    // token surface). The remarks below state this so no reader mistakes the
    // exposure for an oversight — the chats surface's webhook-boolean style.
    // Server-wide MASTER keys (empty ProductId) never travel through this
    // surface: they are not part of any product's key collection and the item
    // read refuses them with the area's plain 404.
    [Description("A product access key — the credential an HSM agent sends in the 'Key' header. The key id IS the credential itself; it is exposed deliberately (web parity — the web UI shows it to users who can see the product).")]
    public sealed record AccessKeyDto
    {
        /// <summary>Key id — the credential itself (the value the agent sends in the Key header). Exposed deliberately: web parity.</summary>
        [Description("Key id — the credential itself (the value the agent sends in the 'Key' header). Exposed deliberately: web parity.")]
        public Guid Id { get; init; }

        /// <summary>The key's display name.</summary>
        [Description("The key's display name.")]
        public string DisplayName { get; init; }

        /// <summary>The owning product's id.</summary>
        [Description("The owning product's id.")]
        public Guid ProductId { get; init; }

        /// <summary>Stored state: 'active', 'expired' or 'blocked' (an expiration that has passed still reads 'active' here until a validation marks it — read expirationTime for the effective picture).</summary>
        [Description("Stored state: 'active', 'expired' or 'blocked'. An expiration that has passed still reads 'active' until a key validation marks it — read expirationTime for the effective picture.")]
        public string State { get; init; }

        /// <summary>The key's permission flags as names: 'canSendSensorData', 'canAddNodes', 'canAddSensors', 'canReadSensorData' (empty when the key holds none).</summary>
        [Description("The key's permission flags as names: 'canSendSensorData', 'canAddNodes', 'canAddSensors', 'canReadSensorData' (empty array when the key holds none).")]
        public string[] Permissions { get; init; }

        /// <summary>Creation time (UTC).</summary>
        [Description("Creation time (UTC).")]
        public DateTime CreationTime { get; init; }

        /// <summary>Expiration time (UTC); far future means the key never expires.</summary>
        [Description("Expiration time (UTC); far future means the key never expires.")]
        public DateTime ExpirationTime { get; init; }

        /// <summary>When the key was last used by an agent (UTC); null when it never was.</summary>
        [Description("When the key was last used by an agent (UTC); null when it never was — the 'has the installed agent connected' signal.")]
        public DateTime? LastUseTime { get; init; }

        /// <summary>The IP address the key was last used from; null when it never was.</summary>
        [Description("The IP address the key was last used from; null when it never was.")]
        public string LastIp { get; init; }

        /// <summary>The id of the user who created the key; null for keys created by the system (e.g. a product's default key).</summary>
        [Description("The id of the user who created the key; null for keys created by the system (e.g. a product's default key).")]
        public Guid? AuthorId { get; init; }
    }
}
