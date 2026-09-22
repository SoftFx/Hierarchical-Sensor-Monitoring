using System;
using System.IO;
using Xunit;

namespace HSMServer.Core.Tests
{
    // The wiki Installation page embeds the reference docker-compose.yml verbatim so admins
    // copy the supported HSM + Caddy setup instead of inventing their own (#1411). This pins
    // the embedded copy to the repository file: changing one without the other fails here.
    public class ReferenceComposeDocTests
    {
        [Fact]
        public void WikiInstallationPage_EmbedsTheRepositoryComposeFileVerbatim()
        {
            var root = FindRepoRoot();

            var wikiPath = Path.Combine(root, "wiki-git", "Installation.md");
            Assert.True(File.Exists(wikiPath), $"{wikiPath} not found: the reference compose copy lives on that page; update this test if the page moved.");

            var compose = Normalize(File.ReadAllText(Path.Combine(root, "docker-compose.yml"))).TrimEnd('\n');
            var wiki = Normalize(File.ReadAllText(wikiPath));

            const string heading = "### Reference docker-compose.yml";
            var section = wiki.IndexOf(heading, StringComparison.Ordinal);
            Assert.True(section >= 0, $"wiki-git/Installation.md has no \"{heading}\" section.");

            // The first yaml block after the heading must be the file itself, not a copy elsewhere on the page.
            var block = wiki.IndexOf("```yaml\n", section, StringComparison.Ordinal);
            Assert.True(block >= 0 && wiki.AsSpan(block).StartsWith("```yaml\n" + compose + "\n```", StringComparison.Ordinal),
                "wiki-git/Installation.md must embed docker-compose.yml verbatim right under \"Reference docker-compose.yml\"; update both together.");
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimStart('\uFEFF');

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
                directory = directory.Parent;

            Assert.True(directory is not null, $"Cannot locate the repository root from {AppContext.BaseDirectory}");

            return directory.FullName;
        }
    }
}
