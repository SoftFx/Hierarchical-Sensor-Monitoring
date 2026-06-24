using HSMServer.Model.Agent;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HSMServer.Core.Tests
{
    public class AgentInstallerBundleTests
    {
        private static readonly AgentBundleOptions _options =
            new("https://hsm.example.com", 44330, "11111111-2222-3333-4444-555555555555", AllowUntrustedCertificate: false);

        [Fact]
        public void ConfigJson_CarriesServerAddressPortAndKey()
        {
            var json = AgentInstallerBundle.BuildConfigJson(_options);

            using var doc = JsonDocument.Parse(json);
            var server = doc.RootElement.GetProperty("server");

            Assert.Equal("https://hsm.example.com", server.GetProperty("address").GetString());
            Assert.Equal(44330, server.GetProperty("port").GetInt32());
            Assert.Equal(_options.AccessKey, server.GetProperty("accessKey").GetString());
            Assert.False(server.GetProperty("allowUntrustedCertificate").GetBoolean());
            Assert.Equal("auto", doc.RootElement.GetProperty("identity").GetProperty("computerName").GetString());
        }

        [Fact]
        public void ConfigJson_OmitsTopCpu_WhenDisabled()
        {
            var json = AgentInstallerBundle.BuildConfigJson(_options); // EnableTopCpu defaults to false

            using var doc = JsonDocument.Parse(json);
            Assert.False(doc.RootElement.TryGetProperty("topCpu", out _));
        }

        [Fact]
        public void ConfigJson_CarriesTopCpu_WhenEnabled()
        {
            var options = _options with { EnableTopCpu = true };

            var json = AgentInstallerBundle.BuildConfigJson(options);

            using var doc = JsonDocument.Parse(json);
            var topCpu = doc.RootElement.GetProperty("topCpu");
            Assert.True(topCpu.GetProperty("enabled").GetBoolean());
            Assert.Equal(60000, topCpu.GetProperty("periodMs").GetInt32());
            Assert.Equal(10, topCpu.GetProperty("count").GetInt32());
            Assert.Equal(1.0, topCpu.GetProperty("minPercent").GetDouble());
        }

        [Fact]
        public void Zip_ContainsExeConfigAndScripts()
        {
            var exeBytes = Encoding.ASCII.GetBytes("MZ-fake-signed-agent-binary");

            var zipBytes = AgentInstallerBundle.BuildZip(exeBytes, _options);

            using var memory = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read);

            var names = archive.Entries.Select(e => e.FullName).ToList();
            Assert.Contains(AgentInstallerBundle.ExeName, names);
            Assert.Contains(AgentInstallerBundle.ConfigName, names);
            Assert.Contains(AgentInstallerBundle.InstallScript, names);
            Assert.Contains(AgentInstallerBundle.UninstallScript, names);
        }

        [Fact]
        public void Zip_KeepsExeByteIdentical()
        {
            var exeBytes = Encoding.ASCII.GetBytes("MZ-fake-signed-agent-binary-\0\x01\x02");

            var zipBytes = AgentInstallerBundle.BuildZip(exeBytes, _options);

            using var memory = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read);

            using var entryStream = archive.GetEntry(AgentInstallerBundle.ExeName).Open();
            using var copy = new MemoryStream();
            entryStream.CopyTo(copy);

            Assert.Equal(exeBytes, copy.ToArray());
        }

        [Fact]
        public void Zip_ConfigEntryCarriesAddressAndKey()
        {
            var zipBytes = AgentInstallerBundle.BuildZip(new byte[] { 0x4D, 0x5A }, _options);

            using var memory = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
            using var reader = new StreamReader(archive.GetEntry(AgentInstallerBundle.ConfigName).Open());

            var json = reader.ReadToEnd();
            Assert.Contains(_options.AccessKey, json);
            Assert.Contains("https://hsm.example.com", json);
        }

        [Fact]
        public void InstallScript_RegistersAndStartsTheService()
        {
            var zipBytes = AgentInstallerBundle.BuildZip(new byte[] { 0x4D, 0x5A }, _options);

            using var memory = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
            using var reader = new StreamReader(archive.GetEntry(AgentInstallerBundle.InstallScript).Open());

            var script = reader.ReadToEnd();
            Assert.Contains("--install", script);
            Assert.Contains("HSMAgent", script);
        }

        // The static packaging scripts (src/agent/packaging/*.cmd, used for the manual-artifact install)
        // must stay identical to what the server generates for the download, so they can't drift.
        // Compared with line endings normalized (the static files' CRLF is an eol-policy concern).
        [Fact]
        public void StaticInstallScript_MatchesGenerated()
        {
            Assert.Equal(
                Normalize(AgentInstallerBundle.BuildInstallScript()),
                Normalize(File.ReadAllText(RepoFile("src/agent/packaging/install-hsmagent-service.cmd"))));
        }

        [Fact]
        public void StaticUninstallScript_MatchesGenerated()
        {
            Assert.Equal(
                Normalize(AgentInstallerBundle.BuildUninstallScript()),
                Normalize(File.ReadAllText(RepoFile("src/agent/packaging/uninstall-hsmagent-service.cmd"))));
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");

        private static string RepoFile(string relative)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            var native = relative.Replace('/', Path.DirectorySeparatorChar);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, native);
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }

            throw new FileNotFoundException($"Could not locate '{relative}' above {AppContext.BaseDirectory}");
        }
    }
}
