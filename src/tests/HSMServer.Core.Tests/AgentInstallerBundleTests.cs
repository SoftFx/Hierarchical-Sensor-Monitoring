using HSMServer.Model.Agent;
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
    }
}
