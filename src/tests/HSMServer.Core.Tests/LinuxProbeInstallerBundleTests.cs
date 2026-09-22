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
        private const string PackageName = "hsm-linux-probe_0.1.0_amd64.deb";
        private const string Key = "11111111-2222-3333-4444-555555555555";
        private const string CaPem = "-----BEGIN CERTIFICATE-----\nTUlJ\n-----END CERTIFICATE-----\n";

        private static readonly LinuxProbeBundleOptions _options = new("https://hsm.example.com", 44330, Key);

        private static readonly byte[] _package = Encoding.ASCII.GetBytes("!<arch>\ndebian-binary\0\x01\x02\xff-fake-deb");


        [Fact]
        public void TarGz_ContainsPackageConfigKeyAndScripts()
        {
            var entries = Read(LinuxProbeInstallerBundle.BuildTarGz(PackageName, _package, _options));

            Assert.Equal(
                new[] { PackageName, "config.json", "access-key", "install.sh", "uninstall.sh" }.OrderBy(n => n),
                entries.Keys.OrderBy(n => n));
        }

        [Fact]
        public void TarGz_CarriesServerCa_OnlyWhenGiven()
        {
            var without = Read(LinuxProbeInstallerBundle.BuildTarGz(PackageName, _package, _options));
            var with = Read(LinuxProbeInstallerBundle.BuildTarGz(PackageName, _package, _options with { ServerCaPem = CaPem }));

            Assert.False(without.ContainsKey(LinuxProbeInstallerBundle.ServerCaName));
            Assert.Equal(CaPem, Encoding.ASCII.GetString(with[LinuxProbeInstallerBundle.ServerCaName].Content));
        }

        [Fact]
        public void TarGz_KeepsPackageByteIdentical()
        {
            var entries = Read(LinuxProbeInstallerBundle.BuildTarGz(PackageName, _package, _options));

            Assert.Equal(_package, entries[PackageName].Content);
        }

        [Fact]
        public void Key_IsOnlyInAccessKeyFile_NeverInConfigOrScripts()
        {
            var entries = Read(LinuxProbeInstallerBundle.BuildTarGz(PackageName, _package, _options with { ServerCaPem = CaPem }));

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
            Assert.Equal("LinuxProbe", hsm.GetProperty("module").GetString());
            Assert.Equal("auto", hsm.GetProperty("computerName").GetString());

            Assert.False(hsm.TryGetProperty("accessKey", out _));
            Assert.False(hsm.TryGetProperty("allowUntrustedCertificate", out _));
        }

        [Fact]
        public void TarEntries_HaveExpectedModesAndRootOwnership()
        {
            var entries = Read(LinuxProbeInstallerBundle.BuildTarGz(PackageName, _package, _options with { ServerCaPem = CaPem }));

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

            // The key is only moved as a file: never printed or read into a variable.
            Assert.DoesNotContain("cat access-key", script);
            Assert.DoesNotContain("$(< access-key", script);
        }

        [Fact]
        public void InstallScript_ReplacesTheExactAutoComputerNameTheConfigCarries()
        {
            // install.sh swaps "computerName": "auto" for the host name with sed; pin the literal it
            // matches to what BuildConfigJson actually writes, so a serializer format change fails here.
            Assert.Contains("\"computerName\": \"auto\"", LinuxProbeInstallerBundle.BuildConfigJson(_options));
            Assert.Contains("s/\\\"computerName\\\": \\\"auto\\\"/", LinuxProbeInstallerBundle.BuildInstallScript());
        }

        [Fact]
        public void UninstallScript_PurgesPackageAndRemovesKeyConfigAndCa()
        {
            var script = LinuxProbeInstallerBundle.BuildUninstallScript();

            Assert.Contains("systemctl disable --now \"$UNIT\"", script);
            Assert.Contains("apt-get purge -y \"$PACKAGE\"", script);
            Assert.Contains("\"$CONFIG_DIR/access-key\"", script);
            Assert.Contains("\"$CONFIG_DIR/config.json\"", script);
            Assert.Contains("rm -f \"$CA_TARGET\"", script);
            Assert.DoesNotContain("curl", script); // never talks to the HSM server
        }

        [Fact]
        public void BundleFileName_FollowsTheProductName()
        {
            Assert.Equal("hsm-linux-probe-garage.tar.gz", LinuxProbeInstallerBundle.BundleFileName("garage"));
        }


        private sealed record Entry(byte[] Content, UnixFileMode Mode, TarEntryType Type, int Uid);

        private static Dictionary<string, Entry> Read(byte[] tarGz)
        {
            var result = new Dictionary<string, Entry>();

            using var memory = new MemoryStream(tarGz);
            using var gzip = new GZipStream(memory, CompressionMode.Decompress);
            using var reader = new TarReader(gzip);

            while (reader.GetNextEntry(copyData: true) is { } entry)
            {
                using var content = new MemoryStream();
                entry.DataStream?.CopyTo(content);
                result.Add(entry.Name, new Entry(content.ToArray(), entry.Mode, entry.EntryType, entry.Uid));
            }

            return result;
        }
    }
}
