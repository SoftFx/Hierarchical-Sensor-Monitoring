using HSMServer.Model.Agent;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HSMServer.Core.Tests
{
    // Per-product Linux probe bundle (#1424): the .tar.gz layout, the byte-identical .deb, the key kept
    // out of config.json, the config schema and the tar entry modes install.sh relies on.
    public class LinuxProbeInstallerBundleTests
    {
        private const string Folder = "hsm-linux-probe-garage";
        private const string PackageName = "hsm-linux-probe_0.1.0_amd64.deb";
        private const string Key = "11111111-2222-3333-4444-555555555555";
        private const string CaPem = "-----BEGIN CERTIFICATE-----\nTUlJ\n-----END CERTIFICATE-----\n";

        private static readonly LinuxProbeBundleOptions _options = new("https://hsm.example.com", 44330, Key);

        private static readonly byte[] _package = Encoding.ASCII.GetBytes("!<arch>\ndebian-binary\0\x01\x02\xff-fake-deb");


        [Fact]
        public void TarGz_ContainsPackageConfigKeyAndScripts()
        {
            var entries = Read(LinuxProbeInstallerBundle.BuildTarGz(Folder, PackageName, _package, _options));

            Assert.Equal(
                new[] { PackageName, "config.json", "access-key", "install.sh", "uninstall.sh" }.OrderBy(n => n),
                entries.Keys.OrderBy(n => n));
        }

        [Fact]
        public void TarGz_CarriesServerCa_OnlyWhenGiven()
        {
            var without = Read(LinuxProbeInstallerBundle.BuildTarGz(Folder, PackageName, _package, _options));
            var with = Read(LinuxProbeInstallerBundle.BuildTarGz(Folder, PackageName, _package, _options with { ServerCaPem = CaPem }));

            Assert.False(without.ContainsKey(LinuxProbeInstallerBundle.ServerCaName));
            Assert.Equal(CaPem, Encoding.ASCII.GetString(with[LinuxProbeInstallerBundle.ServerCaName].Content));
        }

        [Fact]
        public void TarGz_KeepsPackageByteIdentical()
        {
            var entries = Read(LinuxProbeInstallerBundle.BuildTarGz(Folder, PackageName, _package, _options));

            Assert.Equal(_package, entries[PackageName].Content);
        }

        [Fact]
        public void Key_IsOnlyInAccessKeyFile_NeverInConfigOrScripts()
        {
            var entries = Read(LinuxProbeInstallerBundle.BuildTarGz(Folder, PackageName, _package, _options with { ServerCaPem = CaPem }));

            Assert.Equal(Key, Encoding.ASCII.GetString(entries["access-key"].Content).Trim());

            foreach (var (name, entry) in entries.Where(e => e.Key != "access-key"))
                Assert.DoesNotContain(Key, Encoding.UTF8.GetString(entry.Content));
        }

        [Fact]
        public void ConfigJson_HasTheProbeSchemaFields()
        {
            using var doc = JsonDocument.Parse(LinuxProbeInstallerBundle.BuildConfigJson(_options));
            var hsm = doc.RootElement.GetProperty("hsm");

            Assert.Equal("https://hsm.example.com", hsm.GetProperty("address").GetString());
            Assert.Equal(44330, hsm.GetProperty("port").GetInt32());
            Assert.Equal("/run/credentials/hsm-linux-probe.service/access-key", hsm.GetProperty("accessKeyFile").GetString());
            // One product = one host: no computer node (#1493) and the module node `.probe` (#1496),
            // written out so the layout does not depend on the probe's defaults, which were
            // `LinuxProbe` before 0.6.0 and empty in 0.6.0 (#1495).
            Assert.Equal("", hsm.GetProperty("computerName").GetString());
            Assert.Equal(".probe", hsm.GetProperty("module").GetString());

            Assert.False(hsm.TryGetProperty("accessKey", out _));
            Assert.False(hsm.TryGetProperty("allowUntrustedCertificate", out _));
        }

        [Fact]
        public void ConfigJson_OmitsTopCpu_WhenDisabled()
        {
            using var doc = JsonDocument.Parse(LinuxProbeInstallerBundle.BuildConfigJson(_options)); // EnableTopCpu defaults to false

            Assert.False(doc.RootElement.TryGetProperty("topCpu", out _));
        }

        [Fact]
        public void ConfigJson_CarriesTheAgentsTopCpuBlock_WhenEnabled()
        {
            using var doc = JsonDocument.Parse(LinuxProbeInstallerBundle.BuildConfigJson(_options with { EnableTopCpu = true }));
            var topCpu = doc.RootElement.GetProperty("topCpu");

            // The probe reads the agent's keys and defaults at the same top level (#1479).
            Assert.True(topCpu.GetProperty("enabled").GetBoolean());
            Assert.Equal(60000, topCpu.GetProperty("periodMs").GetInt32());
            Assert.Equal(10, topCpu.GetProperty("count").GetInt32());
            Assert.Equal(1.0, topCpu.GetProperty("minPercent").GetDouble());

            using var agent = JsonDocument.Parse(AgentInstallerBundle.BuildConfigJson(new AgentBundleOptions("https://hsm.example.com", 44330, Key, false, EnableTopCpu: true)));
            Assert.Equal(agent.RootElement.GetProperty("topCpu").GetRawText(), topCpu.GetRawText());

            // The rest of the probe config is unchanged by the switch.
            Assert.Equal("https://hsm.example.com", doc.RootElement.GetProperty("hsm").GetProperty("address").GetString());
            Assert.EndsWith("}\n", LinuxProbeInstallerBundle.BuildConfigJson(_options with { EnableTopCpu = true }));
        }

        [Fact]
        public void TopCpu_InstallScriptLiftsProtectProcOnlyWhenEnabled()
        {
            var off = LinuxProbeInstallerBundle.BuildInstallScript();
            var on = LinuxProbeInstallerBundle.BuildInstallScript(enableTopCpu: true);

            // Both scripts decide from the config installed on the host (the bundle's, or the kept one),
            // not from the bundle's switch: a kept config that enables topCpu wins over a bundle without it.
            foreach (var script in new[] { off, on })
            {
                Assert.Contains("TOP_CPU_DROPIN=\"/etc/systemd/system/hsm-linux-probe.service.d/top-cpu.conf\"", script);
                Assert.Contains("if tr -d '\\r\\n' 2>/dev/null < \"$CONFIG_DIR/config.json\" \\\n" +
                                "  | grep -Eq '\"topCpu\"[[:space:]]*:[[:space:]]*\\{[^}]*\"enabled\"[[:space:]]*:[[:space:]]*true'; then", script);
                Assert.Contains("  cat > \"$TOP_CPU_DROPIN\" <<'EOF'", script);
                Assert.Contains("\n[Service]\nProtectProc=default\nEOF\n", script);
                // Anything else (off, missing, unrecognised) removes the drop-in: the safe side.
                Assert.Contains("    rm -f \"$TOP_CPU_DROPIN\"", script);
                // Decided after the config is placed and before systemd is reloaded and the unit (re)started.
                var check = script.IndexOf("top_cpu_on=0", System.StringComparison.Ordinal);
                Assert.True(script.IndexOf("install -m 0644 -o root -g root config.json", System.StringComparison.Ordinal) < check);
                Assert.True(check < script.IndexOf("\nsystemctl daemon-reload\n", System.StringComparison.Ordinal));
                Assert.DoesNotContain("python", script);
                Assert.DoesNotContain("jq ", script);
            }

            // Only a bundle that enables top CPU warns when the kept config does not.
            Assert.Contains("WARNING: this bundle enables top CPU processes, but the existing $CONFIG_DIR/config.json was kept and does not.", on);
            Assert.Contains("sudo ./install.sh --force-config", on);
            Assert.Contains("RUNBOOK.md, 'Top CPU processes'", on);
            Assert.DoesNotContain("WARNING: this bundle enables top CPU", off);

            // The bundle ships the script matching its config.
            var entries = Read(LinuxProbeInstallerBundle.BuildTarGz(Folder, PackageName, _package, _options with { EnableTopCpu = true }));
            Assert.Equal(on, Encoding.UTF8.GetString(entries["install.sh"].Content));
            Assert.Contains("\"topCpu\"", Encoding.UTF8.GetString(entries["config.json"].Content));

            Assert.Contains("rm -f \"/etc/systemd/system/hsm-linux-probe.service.d/top-cpu.conf\"", LinuxProbeInstallerBundle.BuildUninstallScript());
        }

        [Fact]
        public void TarEntries_HaveExpectedModesAndRootOwnership()
        {
            var entries = Read(LinuxProbeInstallerBundle.BuildTarGz(Folder, PackageName, _package, _options with { ServerCaPem = CaPem }));

            const UnixFileMode rwxr_xr_x = (UnixFileMode)0b111_101_101; // 0755
            const UnixFileMode rw_r__r__ = (UnixFileMode)0b110_100_100; // 0644
            const UnixFileMode rw_______ = (UnixFileMode)0b110_000_000; // 0600

            Assert.Equal(rwxr_xr_x, entries["install.sh"].Mode);
            Assert.Equal(rwxr_xr_x, entries["uninstall.sh"].Mode);
            Assert.Equal(rw_______, entries["access-key"].Mode);
            Assert.Equal(rw_r__r__, entries["config.json"].Mode);
            Assert.Equal(rw_r__r__, entries[PackageName].Mode);
            Assert.Equal(rw_r__r__, entries["server-ca.pem"].Mode);

            Assert.All(entries.Values, e => Assert.Equal(TarEntryType.RegularFile, e.Type));
            Assert.All(entries.Values, e => Assert.Equal(0, e.Uid));
            Assert.All(entries.Values, e => Assert.Equal(0, e.Gid));
        }

        [Fact]
        public void TarGz_KeepsEverythingInOneTopLevelFolder()
        {
            var names = new List<string>();
            using (var gzip = new GZipStream(new MemoryStream(LinuxProbeInstallerBundle.BuildTarGz(Folder, PackageName, _package, _options)), CompressionMode.Decompress))
            using (var reader = new TarReader(gzip))
                while (reader.GetNextEntry() is { } entry)
                    names.Add(entry.Name);

            Assert.Equal(Folder + "/", names[0]);
            Assert.All(names, n => Assert.StartsWith(Folder + "/", n));
            Assert.Equal(Folder, LinuxProbeInstallerBundle.BundleFolderName("garage"));
        }

        [Fact]
        public void Scripts_UseLfLineEndingsAndStrictMode()
        {
            foreach (var script in new[] { LinuxProbeInstallerBundle.BuildInstallScript(), LinuxProbeInstallerBundle.BuildUninstallScript() })
            {
                Assert.DoesNotContain("\r", script);
                Assert.StartsWith("#!/usr/bin/env bash\n", script);
                Assert.Contains("\nset -euo pipefail\n", script);
                Assert.Contains("\"$(id -u)\" -ne 0", script);
            }
        }

        [Fact]
        public void InstallScript_InstallsPackageKeyCaAndEnablesUnit()
        {
            var script = LinuxProbeInstallerBundle.BuildInstallScript();

            Assert.Contains("apt-get install -y", script);
            Assert.Contains("./hsm-linux-probe_*.deb", script);
            Assert.Contains("install -m 0400 -o root -g root access-key \"$CONFIG_DIR/access-key\"", script);
            Assert.Contains("--force-config", script);
            Assert.Contains("/usr/local/share/ca-certificates/hsm-server.crt", script);
            Assert.Contains("update-ca-certificates", script);
            Assert.Contains("systemctl enable --now \"$UNIT\"", script);
            Assert.Contains("systemctl status --no-pager \"$UNIT\"", script);
            Assert.Contains("shred -u access-key", script);
            Assert.Contains("apt-get update", script);
            Assert.Contains("\"${packages[0]}\" ca-certificates", script); // unconditionally: the system store is always the trust source
            Assert.Contains("still contains the access key", script);

            // The extracted key is cleaned up on every exit once it is installed, not only on success.
            // Armed before the key is copied, so even a failing copy cleans up.
            var trap = script.IndexOf("trap '", System.StringComparison.Ordinal);
            Assert.True(trap > 0);
            Assert.True(trap < script.IndexOf("install -m 0400", System.StringComparison.Ordinal));

            // The bundle's config is installed as is: no host name is written into it any more.
            Assert.DoesNotContain("computerName", script);
            Assert.DoesNotContain("hostname", script);
            // The layout has no host node, so the rule that keeps two hosts apart is said out loud.
            Assert.Contains("one product per host", script);
            Assert.Contains("install -m 0644 -o root -g root config.json \"$CONFIG_DIR/config.json\"", script);

            // The key is only moved as a file: never printed or read into a variable.
            Assert.DoesNotContain("cat access-key", script);
            Assert.DoesNotContain("$(< access-key", script);
        }

        [Fact]
        public void UninstallScript_PurgesPackageAndRemovesKeyConfigAndCa()
        {
            var script = LinuxProbeInstallerBundle.BuildUninstallScript();

            Assert.Contains("systemctl disable --now \"$UNIT\"", script);
            Assert.Contains("apt-get purge -y \"$PACKAGE\"", script);
            Assert.Contains("shred -u \"$CONFIG_DIR/access-key\" 2>/dev/null || rm -f \"$CONFIG_DIR/access-key\"", script);
            Assert.Contains("\"$CONFIG_DIR/config.json\"", script);
            Assert.Contains("rm -f \"$CA_TARGET\"", script);
            Assert.DoesNotContain("--fresh", script); // would wipe unrelated hand-made links in /etc/ssl/certs
            Assert.DoesNotContain("curl", script); // never talks to the HSM server
        }

        [Fact]
        public void BundleFileName_FollowsTheProductName()
        {
            Assert.Equal("hsm-linux-probe-garage.tar.gz", LinuxProbeInstallerBundle.BundleFileName("garage"));
        }

        [Theory]
        [InlineData("Prod (EU);poweroff", "Prod_EU_poweroff")]
        [InlineData("a && rm -rf *", "a_rm_-rf")]
        [InlineData("Garage server", "Garage_server")]
        [InlineData("--help", "help")]
        [InlineData("..", "product")]
        [InlineData("#;&*()", "product")]
        [InlineData("", "product")]
        [InlineData(null, "product")]
        [InlineData("v1.2_rc-3", "v1.2_rc-3")]
        public void BundleFolderName_IsShellSafe(string productName, string expectedSuffix)
        {
            var folder = LinuxProbeInstallerBundle.BundleFolderName(productName);

            Assert.Equal("hsm-linux-probe-" + expectedSuffix, folder);
            Assert.Matches("^[A-Za-z0-9._-]+$", folder);
        }


        private sealed record Entry(byte[] Content, UnixFileMode Mode, TarEntryType Type, int Uid, int Gid);

        private static Dictionary<string, Entry> Read(byte[] tarGz)
        {
            var result = new Dictionary<string, Entry>();

            using var memory = new MemoryStream(tarGz);
            using var gzip = new GZipStream(memory, CompressionMode.Decompress);
            using var reader = new TarReader(gzip);

            while (reader.GetNextEntry(copyData: true) is { } entry)
            {
                // Entries sit under one top-level folder; key them by their name inside it.
                Assert.StartsWith(Folder + "/", entry.Name);
                if (entry.EntryType == TarEntryType.Directory)
                    continue;

                using var content = new MemoryStream();
                entry.DataStream?.CopyTo(content);
                result.Add(entry.Name[(Folder.Length + 1)..], new Entry(content.ToArray(), entry.Mode, entry.EntryType, entry.Uid, entry.Gid));
            }

            return result;
        }
    }
}
