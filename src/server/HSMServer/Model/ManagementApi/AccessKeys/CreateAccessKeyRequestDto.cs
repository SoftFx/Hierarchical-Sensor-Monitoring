using System.ComponentModel;

namespace HSMServer.Model.ManagementApi.AccessKeys
{
    /// <summary>
    /// The create-access-key request body. Validation (the web form's display
    /// name rules, the permission names) is owned by the administration
    /// service, not by binding attributes — the REST and MCP transports share
    /// one validation decision with the web UI's error strings.
    /// </summary>
    public sealed class CreateAccessKeyRequestDto
    {
        /// <summary>The new key's display name: 1..100 characters, no control
        /// or bidirectional-override characters, unique among the product's
        /// keys. Surrounding whitespace is trimmed.</summary>
        [Description("The new key's display name: 1..100 characters, no control or bidirectional-override characters, unique among the product's keys. Surrounding whitespace is trimmed.")]
        public string DisplayName { get; init; }

        /// <summary>The key's permission names ('canSendSensorData', 'canAddNodes', 'canAddSensors', 'canReadSensorData'); at least one. Omitted or null = the agent profile ('canSendSensorData', 'canAddNodes', 'canAddSensors') — enough for an HSM agent, no read-back.</summary>
        [Description("The key's permission names ('canSendSensorData', 'canAddNodes', 'canAddSensors', 'canReadSensorData'); at least one. Omitted or null = the agent profile ('canSendSensorData', 'canAddNodes', 'canAddSensors') — enough for an HSM agent, no read-back.")]
        public string[] Permissions { get; init; }
    }
}
