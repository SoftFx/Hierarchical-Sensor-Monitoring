using System;
using System.ComponentModel;

namespace HSMServer.Model.ManagementApi.AgentInstaller
{
    /// <summary>
    /// A one-time download link for a product's agent-installer bundle (#1788):
    /// everything an orchestrator needs to fetch the bundle ON THE TARGET
    /// MACHINE without the management bearer — the URL carries a single-use
    /// short-lived token, and the sha256 lets it verify the bytes it curled.
    /// </summary>
    [Description("A one-time download link for the product's agent-installer bundle: fetch `url` on the target machine (no Authorization header — the token in the URL is the credential, valid once), verify the bytes against `sha256`, unpack, run the install script. The link expires at `expiresAt`.")]
    public sealed record AgentInstallerLinkDto
    {
        /// <summary>The absolute download URL — single use, short-lived; works without an Authorization header.</summary>
        [Description("The absolute download URL — single use, short-lived; works without an Authorization header.")]
        public string Url { get; init; }

        /// <summary>When the link dies (UTC ISO 8601) whether or not it was redeemed.</summary>
        [Description("When the link dies (UTC ISO 8601) whether or not it was redeemed.")]
        public DateTime ExpiresAt { get; init; }

        /// <summary>The bundle's file name (a .zip for windows, a .tar.gz for linux).</summary>
        [Description("The bundle's file name (a .zip for windows, a .tar.gz for linux).")]
        public string FileName { get; init; }

        /// <summary>The bundle's size in bytes.</summary>
        [Description("The bundle's size in bytes.")]
        public long Size { get; init; }

        /// <summary>The bundle's SHA-256 (lowercase hex) — verify the downloaded bytes against it.</summary>
        [Description("The bundle's SHA-256 (lowercase hex) — verify the downloaded bytes against it.")]
        public string Sha256 { get; init; }
    }
}
