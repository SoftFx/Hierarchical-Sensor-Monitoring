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

            var compose = Normalize(File.ReadAllText(Path.Combine(root, "docker-compose.yml"))).TrimEnd('\n');
            var wiki = Normalize(File.ReadAllText(Path.Combine(root, "wiki-git", "Installation.md")));

            Assert.True(wiki.Contains("```yaml\n" + compose + "\n```", StringComparison.Ordinal),
                "wiki-git/Installation.md must embed docker-compose.yml verbatim (section \"Reference docker-compose.yml\"); update both together.");
        }


        private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimStart('﻿');

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
