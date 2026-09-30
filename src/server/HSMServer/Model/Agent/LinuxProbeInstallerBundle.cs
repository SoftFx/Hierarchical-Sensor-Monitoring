using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HSMServer.Model.Agent
{
    /// <summary>
    /// Parameters baked into one per-product Linux probe bundle. <see cref="AccessKey"/> goes into its
    /// own <c>access-key</c> file, never into config.json. <see cref="ServerCaPem"/> is the server's
    /// public certificate, or null to leave <c>server-ca.pem</c> out of the bundle. <see cref="EnableTopCpu"/>
    /// adds the agent's <c>topCpu</c> block, as <see cref="AgentBundleOptions.EnableTopCpu"/> does (#1479).
    /// </summary>
    public sealed record LinuxProbeBundleOptions(string ServerAddress, int Port, string AccessKey, string ServerCaPem = null, bool EnableTopCpu = false);

    /// <summary>
    /// Builds the downloadable HSM Linux probe bundle (#1424, initiative linux-docker-probe §4.6): a
    /// .tar.gz of the byte-identical released .deb + a generated config.json (server address, no key) +
    /// the product key in its own file + optionally the server's public leaf certificate + install/uninstall
    /// scripts. The Linux sibling of <see cref="AgentInstallerBundle"/>; pure and side-effect-free so it
    /// is unit-tested without a web host.
    /// </summary>
    public static class LinuxProbeInstallerBundle
    {
        public const string PackagePrefix = "hsm-linux-probe_";
        public const string PackageExtension = ".deb";
        public const string ConfigName = "config.json";
        public const string KeyName = "access-key";
        public const string ServerCaName = "server-ca.pem";
        public const string InstallScript = "install.sh";
        public const string UninstallScript = "uninstall.sh";

        /// <summary>Directory under wwwroot the server build stages the pinned probe .deb into.</summary>
        public const string StagingFolder = "probe";

        /// <summary>The unit's drop-in directory, and the drop-in the top-CPU switch writes there (#1479).</summary>
        public const string DropInDirectory = "/etc/systemd/system/hsm-linux-probe.service.d";
        public const string TopCpuDropIn = DropInDirectory + "/top-cpu.conf";

        /// <summary>Where the systemd unit's LoadCredential= exposes the key to the probe (§4.3).</summary>
        public const string AccessKeyCredentialPath = "/run/credentials/hsm-linux-probe.service/access-key";

        public const string NotStagedMessage =
            "The Linux probe package is not available on this server yet. The server build stages it from the probe-v* release pinned in probe-release.txt into wwwroot/probe/.";

        public const UnixFileMode ScriptMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

        public const UnixFileMode DataMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

        // The key is a bearer credential: owner-only even before install.sh moves it into place.
        public const UnixFileMode KeyMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };


        /// <summary>
        /// The one top-level directory every entry sits in, so extracting never scatters files, a key
        /// included, into the current directory or mixes two products' bundles.
        /// </summary>
        public static string BundleFolderName(string productName) => $"hsm-linux-probe-{ShellSafeName(productName)}";

        /// <summary>Download file name for a product.</summary>
        public static string BundleFileName(string productName) => BundleFolderName(productName) + ".tar.gz";

        /// <summary>
        /// The product name as it may appear in the folder the operator types into a root shell
        /// (<c>sudo ./hsm-linux-probe-&lt;product&gt;/install.sh</c>). Product names allow ; &amp; * ( ) #,
        /// so everything outside [A-Za-z0-9._-] becomes '_', runs collapse, leading '.'/'-' go (no hidden
        /// folder, no option-like token), and an empty result falls back to "product".
        /// </summary>
        public static string ShellSafeName(string productName)
        {
            var builder = new StringBuilder();
            foreach (var c in (productName ?? string.Empty).Trim())
            {
                var safe = (c is >= 'a' and <= 'z') || (c is >= 'A' and <= 'Z') || (c is >= '0' and <= '9') || c is '.' or '_' or '-';
                var next = safe ? c : '_';
                if (next == '_' && builder.Length > 0 && builder[^1] == '_')
                    continue;
                builder.Append(next);
            }

            var name = builder.ToString().TrimStart('.', '-', '_').TrimEnd('_');
            return name.Length == 0 ? "product" : name;
        }

        /// <summary>
        /// Picks the staged package among the file names in wwwroot/probe/. Staging clears the folder
        /// first, so exactly one <c>hsm-linux-probe_*.deb</c> is expected; none or several (an
        /// ambiguous, half-cleaned folder) return null, which the endpoint reports as not staged.
        /// </summary>
        public static string SelectStagedPackage(IEnumerable<string> fileNames)
        {
            var packages = fileNames
                .Select(Path.GetFileName)
                .Where(n => n.StartsWith(PackagePrefix, StringComparison.Ordinal) && n.EndsWith(PackageExtension, StringComparison.Ordinal))
                .ToList();

            return packages.Count == 1 ? packages[0] : null;
        }

        /// <summary>
        /// Returns why the resolved address cannot be baked into a probe config, or null when it can.
        /// The probe refuses plaintext (its config parser rejects http://), so a bundle pointing at an
        /// http:// address would install and then never connect.
        /// </summary>
        public static string ValidateServerAddress(string address)
        {
            if (address is not null && address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return null;

            return $"The Linux probe only connects over HTTPS, but this server's agent connection address is '{address}'. " +
                   "Set Configuration > Agent > Agent connection URL to the https:// address clients reach the Sensor API at.";
        }

        /// <summary>
        /// The generated config.json in the probe schema. It references the key by path only, and
        /// spells out the layout — no computer node and the module node <c>.probe</c> (#1493, #1496)
        /// — rather than relying on the probe's defaults, which differ before 0.6.1 (#1495).
        /// </summary>
        public static string BuildConfigJson(LinuxProbeBundleOptions options)
        {
            var config = new
            {
                hsm = new
                {
                    address = options.ServerAddress,
                    port = options.Port,
                    accessKeyFile = AccessKeyCredentialPath,
                    computerName = "",
                    module = ".probe",
                },
            };

            var json = JsonSerializer.SerializeToNode(config).AsObject();

            // "Report top processes by CPU" (Configuration → Agent): the same block the agent bundle
            // writes, at the same top level of the probe's config (#1479).
            if (options.EnableTopCpu)
                json["topCpu"] = JsonSerializer.SerializeToNode(AgentInstallerBundle.TopCpuBlock);

            return json.ToJsonString(_jsonOptions) + "\n";
        }

        /// <summary>
        /// The installer. Runs as root, installs the package, places config + key + CA, enables the unit.
        /// The key is only ever handled as a file: never echoed, never an argument. With
        /// <paramref name="enableTopCpu"/> it also writes the <see cref="TopCpuDropIn"/> drop-in.
        /// </summary>
        public static string BuildInstallScript(bool enableTopCpu = false)
        {
            return Join(
                "#!/usr/bin/env bash",
                "# HSM Linux probe: install this preconfigured bundle.",
                "#   tar xzf hsm-linux-probe-<product>.tar.gz && sudo ./hsm-linux-probe-<product>/install.sh",
                "# Re-running it upgrades the package and keeps an existing /etc/hsm-linux-probe/config.json;",
                "# pass --force-config to replace that config with the one in this bundle.",
                "# One product per host: the probe reports at the product root, so a second host with the",
                "# same product's bundle would write into the same sensors.",
                "set -euo pipefail",
                "",
                "UNIT=hsm-linux-probe",
                "CONFIG_DIR=/etc/hsm-linux-probe",
                "CA_TARGET=/usr/local/share/ca-certificates/hsm-server.crt",
                "",
                "force_config=0",
                "for arg in \"$@\"; do",
                "  case \"$arg\" in",
                "    --force-config) force_config=1 ;;",
                "    -h|--help) echo \"Usage: sudo ./install.sh [--force-config]\"; exit 0 ;;",
                "    *) echo \"Unknown option: $arg (usage: sudo ./install.sh [--force-config])\" >&2; exit 2 ;;",
                "  esac",
                "done",
                "",
                "if [ \"$(id -u)\" -ne 0 ]; then",
                "  echo \"ERROR: run this script as root: sudo ./install.sh\" >&2",
                "  exit 1",
                "fi",
                "",
                "cd -- \"$(dirname -- \"${BASH_SOURCE[0]}\")\"",
                "",
                "shopt -s nullglob",
                "packages=(./" + PackagePrefix + "*" + PackageExtension + ")",
                "shopt -u nullglob",
                "if [ \"${#packages[@]}\" -ne 1 ]; then",
                "  echo \"ERROR: expected exactly one " + PackagePrefix + "*" + PackageExtension + " next to this script.\" >&2",
                "  exit 1",
                "fi",
                "if [ ! -f " + ConfigName + " ]; then",
                "  echo \"ERROR: " + ConfigName + " not found next to this script.\" >&2",
                "  exit 1",
                "fi",
                "if [ ! -f " + KeyName + " ] && [ ! -f \"$CONFIG_DIR/" + KeyName + "\" ]; then",
                "  echo \"ERROR: " + KeyName + " not found next to this script and no key is installed yet.\" >&2",
                "  exit 1",
                "fi",
                "",
                "install -d -m 0755 -o root -g root \"$CONFIG_DIR\"",
                "",
                "# Config and key go in BEFORE the package, so the unit's first start already finds them.",
                "if [ ! -f \"$CONFIG_DIR/" + ConfigName + "\" ] || [ \"$force_config\" -eq 1 ]; then",
                "  install -m 0644 -o root -g root " + ConfigName + " \"$CONFIG_DIR/" + ConfigName + "\"",
                "  echo \"Config installed to $CONFIG_DIR/" + ConfigName + ".\"",
                "else",
                "  echo \"Keeping the existing $CONFIG_DIR/" + ConfigName + " (pass --force-config to replace it).\"",
                "fi",
                "",
                "if [ -f " + KeyName + " ]; then",
                "  # From here on the installed copy is the only one that should remain, whatever fails later.",
                "  trap 'if [ -f " + KeyName + " ]; then shred -u " + KeyName + " 2>/dev/null || rm -f " + KeyName + "; fi' EXIT",
                "  install -m 0400 -o root -g root " + KeyName + " \"$CONFIG_DIR/" + KeyName + "\"",
                "  echo \"Access key installed to $CONFIG_DIR/" + KeyName + " (root:root 0400).\"",
                "else",
                "  echo \"Keeping the existing access key in $CONFIG_DIR/" + KeyName + ".\"",
                "fi",
                "",
                "export DEBIAN_FRONTEND=noninteractive",
                "# A fresh or stale package index cannot resolve the package's dependencies.",
                "# An unreachable mirror is not fatal on its own: the install below fails loudly if it matters.",
                "apt-get update || echo \"WARNING: apt-get update failed; installing with the current package index.\" >&2",
                "# --force-confold keeps the config placed above instead of the package's placeholder skeleton.",
                "# ca-certificates always: the probe verifies the server against the system trust store, which a",
                "# minimal host lacks, whether or not this bundle ships server-ca.pem (behind a public-CA proxy it does not).",
                "apt-get install -y -o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold \\",
                "  \"${packages[0]}\" ca-certificates",
                "",
                "if [ -f " + ServerCaName + " ]; then",
                "  install -d -m 0755 -o root -g root \"$(dirname \"$CA_TARGET\")\"",
                "  install -m 0644 -o root -g root " + ServerCaName + " \"$CA_TARGET\"",
                "  update-ca-certificates",
                "  echo \"Server certificate added to the system trust store ($CA_TARGET).\"",
                "  # Exactly this certificate is trusted: when the server's certificate is renewed, download the bundle",
                "  # again and re-run install.sh.",
                "  echo \"It is valid until $(openssl x509 -noout -enddate -in " + ServerCaName + " 2>/dev/null | cut -d= -f2 || echo 'unknown');\"",
                "  echo \"after the server certificate is renewed, download the bundle again and re-run install.sh.\"",
                "fi",
                "",
                TopCpuDropInScript(enableTopCpu),
                "",
                "systemctl daemon-reload",
                "# A failed start is judged by the status check below, which prints the diagnostics.",
                "systemctl enable --now \"$UNIT\" || true",
                "# Also restart an already running probe so a reinstall picks up the new key, config and CA.",
                "systemctl restart \"$UNIT\" || true",
                "",
                "# Give a probe that cannot start (bad config, unreadable key) time to exit before judging.",
                "sleep 3",
                "systemctl status --no-pager \"$UNIT\" || true",
                "if systemctl is-active --quiet \"$UNIT\"; then",
                "  echo \"HSM Linux probe installed and running.\"",
                "  echo \"NOTE: one product per host: its sensors sit at the product root; do not install this product's bundle on a second host.\"",
                "  echo \"NOTE: the downloaded hsm-linux-probe-*.tar.gz still contains the access key; delete it (shred -u <file>).\"",
                "else",
                "  echo \"WARNING: $UNIT is not active; see: journalctl -u $UNIT\" >&2",
                "  exit 1",
                "fi");
        }

        /// <summary>
        /// The uninstaller: stops and purges the probe, removes the key, config and CA it installed.
        /// It never contacts the HSM server, so the sensor history stays where it is.
        /// </summary>
        public static string BuildUninstallScript()
        {
            return Join(
                "#!/usr/bin/env bash",
                "# HSM Linux probe: remove what install.sh set up on this host.",
                "# Sensor history on the HSM server is not touched; delete it from the HSM UI if wanted.",
                "set -euo pipefail",
                "",
                "UNIT=hsm-linux-probe",
                "PACKAGE=hsm-linux-probe",
                "CONFIG_DIR=/etc/hsm-linux-probe",
                "CA_TARGET=/usr/local/share/ca-certificates/hsm-server.crt",
                "",
                "if [ \"$(id -u)\" -ne 0 ]; then",
                "  echo \"ERROR: run this script as root: sudo ./uninstall.sh\" >&2",
                "  exit 1",
                "fi",
                "",
                "systemctl disable --now \"$UNIT\" 2>/dev/null || true",
                "",
                "if dpkg -s \"$PACKAGE\" >/dev/null 2>&1; then",
                "  DEBIAN_FRONTEND=noninteractive apt-get purge -y \"$PACKAGE\"",
                "fi",
                "",
                "# The key is a bearer credential: destroy it the way install.sh destroys the extracted copy.",
                "if [ -f \"$CONFIG_DIR/" + KeyName + "\" ]; then",
                "  shred -u \"$CONFIG_DIR/" + KeyName + "\" 2>/dev/null || rm -f \"$CONFIG_DIR/" + KeyName + "\"",
                "fi",
                "rm -f \"$CONFIG_DIR/" + ConfigName + "\" \"$CONFIG_DIR/" + ConfigName + ".dpkg-dist\" \"$CONFIG_DIR/" + ConfigName + ".dpkg-old\"",
                "rmdir \"$CONFIG_DIR\" 2>/dev/null || true",
                "# The top-CPU drop-in install.sh may have written (a package older than 0.7.0 does not remove it).",
                "rm -f \"" + TopCpuDropIn + "\"",
                "rmdir \"" + DropInDirectory + "\" 2>/dev/null || true",
                "",
                "if [ -f \"$CA_TARGET\" ]; then",
                "  rm -f \"$CA_TARGET\"",
                "  update-ca-certificates",
                "fi",
                "",
                "systemctl daemon-reload",
                "echo \"HSM Linux probe uninstalled. Its sensor history on the HSM server is kept.\"");
        }

        /// <summary>
        /// Assembles the .tar.gz in memory under one <paramref name="folderName"/>/ directory: the
        /// unmodified .deb (under its release file name), the generated config, the key file, the
        /// optional CA certificate and both scripts (mode 0755). Fastest compression: the .deb, by far
        /// the largest entry, is already compressed.
        /// </summary>
        public static byte[] BuildTarGz(string folderName, string packageFileName, byte[] package, LinuxProbeBundleOptions options)
        {
            var modified = DateTimeOffset.UtcNow;
            var prefix = folderName + "/";

            using var memory = new MemoryStream();
            using (var gzip = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true))
            using (var tar = new TarWriter(gzip, TarEntryFormat.Ustar, leaveOpen: true))
            {
                AddEntry(tar, TarEntryType.Directory, prefix, null, ScriptMode, modified);
                AddEntry(tar, TarEntryType.RegularFile, prefix + packageFileName, package, DataMode, modified);
                AddEntry(tar, TarEntryType.RegularFile, prefix + ConfigName, Encoding.UTF8.GetBytes(BuildConfigJson(options)), DataMode, modified);
                AddEntry(tar, TarEntryType.RegularFile, prefix + KeyName, Encoding.ASCII.GetBytes(options.AccessKey + "\n"), KeyMode, modified);

                if (!string.IsNullOrEmpty(options.ServerCaPem))
                    AddEntry(tar, TarEntryType.RegularFile, prefix + ServerCaName, Encoding.ASCII.GetBytes(options.ServerCaPem), DataMode, modified);

                AddEntry(tar, TarEntryType.RegularFile, prefix + InstallScript, Encoding.UTF8.GetBytes(BuildInstallScript(options.EnableTopCpu)), ScriptMode, modified);
                AddEntry(tar, TarEntryType.RegularFile, prefix + UninstallScript, Encoding.UTF8.GetBytes(BuildUninstallScript()), ScriptMode, modified);
            }

            return memory.ToArray();
        }

        private static void AddEntry(TarWriter tar, TarEntryType type, string name, byte[] content, UnixFileMode mode, DateTimeOffset modified)
        {
            using var data = content is null ? null : new MemoryStream(content, writable: false);

            var entry = new UstarTarEntry(type, name)
            {
                Mode = mode,
                ModificationTime = modified,
                Uid = 0,
                Gid = 0,
                UserName = "root",
                GroupName = "root",
            };

            // A directory entry carries no data and rejects even a null stream.
            if (data is not null)
                entry.DataStream = data;

            tar.WriteEntry(entry);
        }

        /// <summary>
        /// The install.sh step for "Report top processes by CPU" (#1479). The unit mounts /proc with
        /// <c>ProtectProc=invisible</c>, which hides every process but the probe's own, and the top-CPU
        /// sensors read <c>/proc/&lt;pid&gt;/stat</c> of all of them — so an enabling bundle lifts that one
        /// setting for this unit with a drop-in. A bundle without the switch leaves any drop-in alone
        /// (a hand-enabled <c>topCpu</c> keeps working); uninstall.sh and the package's postrm remove it.
        /// </summary>
        private static string TopCpuDropInScript(bool enableTopCpu)
        {
            if (!enableTopCpu)
                return "# \"Report top processes by CPU\" is off in this bundle: the unit keeps ProtectProc=invisible.";

            return string.Join("\n",
                "# \"Report top processes by CPU\" is on: let the probe see every process in /proc (see the drop-in).",
                "install -d -m 0755 -o root -g root \"" + DropInDirectory + "\"",
                "cat > \"" + TopCpuDropIn + "\" <<'EOF'",
                "# Written by the HSM Linux probe bundle's install.sh: \"Report top processes by CPU\" is on.",
                "# The unit's ProtectProc=invisible hides every process but the probe's own from /proc, and the",
                "# top-CPU sensors read /proc/<pid>/stat of every process. Delete this file, then",
                "# systemctl daemon-reload && systemctl restart hsm-linux-probe, to hide them again.",
                "[Service]",
                "ProtectProc=default",
                "EOF",
                "chmod 0644 \"" + TopCpuDropIn + "\"",
                "echo \"Top CPU processes: the probe may read every process's /proc entry (" + TopCpuDropIn + ").\"");
        }

        // Shell scripts need LF line endings, whatever the server OS is.
        private static string Join(params string[] lines) => string.Join("\n", lines) + "\n";
    }
}
