using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace HSMServer.Model.Agent
{
    /// <summary>
    /// Parameters baked into one per-product agent bundle. <see cref="AccessKey"/> is the product's
    /// access-key GUID (string) the agent sends in the <c>Key</c> header. <see cref="EnableTopCpu"/>
    /// adds a <c>topCpu</c> block so the installed agent also reports the top processes by CPU (#1175).
    /// </summary>
    public sealed record AgentBundleOptions(string ServerAddress, int Port, string AccessKey, bool AllowUntrustedCertificate, bool EnableTopCpu = false);

    /// <summary>
    /// Builds the downloadable HSM Agent bundle (epic #1167, W7): a zip of the byte-identical signed
    /// <c>hsm-agent.exe</c> + a generated <c>config.json</c> (server address + key) + silent
    /// install/uninstall scripts. The exe is NEVER modified — all per-product data lives in
    /// <c>config.json</c> (Authenticode invariant). Install is 100% native C++ (the exe self-installs);
    /// no .NET/MSI on the client. Pure + side-effect-free so it is unit-tested without a web host.
    /// </summary>
    public static class AgentInstallerBundle
    {
        public const string ExeName = "hsm-agent.exe";
        public const string ConfigName = "config.json";
        public const string InstallScript = "install-hsmagent-service.cmd";
        public const string UninstallScript = "uninstall-hsmagent-service.cmd";

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        /// <summary>The generated config.json — matches the agent's config schema (only address + key required).</summary>
        public static string BuildConfigJson(AgentBundleOptions options)
        {
            // A Dictionary (not an anonymous type) so the optional topCpu block can be added
            // conditionally; each value's own property names serialize verbatim (camelCase as written).
            var config = new Dictionary<string, object>
            {
                ["server"] = new
                {
                    address = options.ServerAddress,
                    port = options.Port,
                    accessKey = options.AccessKey,
                    allowUntrustedCertificate = options.AllowUntrustedCertificate,
                },
                ["identity"] = new
                {
                    computerName = "auto",
                },
            };

            if (options.EnableTopCpu)
            {
                // Server-side opt-in (Configuration → Agent). Ship the agent's topCpu defaults so the
                // installed service reports the top processes by CPU once a minute. NOTE: these defaults
                // mirror AgentConfig in the agent (src/agent/include/agent/config.hpp) — keep in sync.
                config["topCpu"] = new
                {
                    enabled = true,
                    periodMs = 60000,
                    minPercent = 1.0,
                    count = 10,
                };
            }

            return JsonSerializer.Serialize(config, _jsonOptions);
        }

        /// <summary>Self-elevating silent installer: copy exe + config, register the service, start it.</summary>
        public static string BuildInstallScript()
        {
            return Join(
                "@echo off",
                "setlocal",
                ":: Self-elevate if not already running as administrator.",
                "net session >nul 2>&1",
                "if %errorlevel% neq 0 (",
                "  echo ERROR: Run this script as Administrator ^(right-click ^> Run as administrator^).",
                "  pause",
                "  exit /b 1",
                ")",
                "set \"INSTALL_DIR=%ProgramFiles%\\HSM Agent\"",
                "set \"DATA_DIR=%ProgramData%\\HSM Agent\"",
                "if not exist \"%INSTALL_DIR%\" mkdir \"%INSTALL_DIR%\"",
                "if not exist \"%DATA_DIR%\" mkdir \"%DATA_DIR%\"",
                "copy /Y \"%~dp0" + ExeName + "\" \"%INSTALL_DIR%\\" + ExeName + "\" >nul",
                "copy /Y \"%~dp0" + ConfigName + "\" \"%DATA_DIR%\\" + ConfigName + "\" >nul",
                "\"%INSTALL_DIR%\\" + ExeName + "\" --install",
                "if %errorlevel% neq 0 (",
                "  echo HSM Agent install failed. & pause & exit /b 1",
                ")",
                "sc start HSMAgent",
                "echo HSM Agent installed and started.",
                "pause",
                "endlocal");
        }

        /// <summary>Self-elevating silent uninstaller: deregister the service, remove the installed exe.</summary>
        public static string BuildUninstallScript()
        {
            return Join(
                "@echo off",
                "setlocal",
                "net session >nul 2>&1",
                "if %errorlevel% neq 0 (",
                "  echo ERROR: Run this script as Administrator ^(right-click ^> Run as administrator^).",
                "  pause",
                "  exit /b 1",
                ")",
                "set \"INSTALL_DIR=%ProgramFiles%\\HSM Agent\"",
                "if exist \"%INSTALL_DIR%\\" + ExeName + "\" \"%INSTALL_DIR%\\" + ExeName + "\" --uninstall",
                "if exist \"%INSTALL_DIR%\\" + ExeName + "\" del /Q \"%INSTALL_DIR%\\" + ExeName + "\"",
                "echo HSM Agent uninstalled.",
                "endlocal");
        }

        /// <summary>Assemble the zip in memory: the unmodified exe + generated config + install scripts.</summary>
        public static byte[] BuildZip(byte[] agentExe, AgentBundleOptions options)
        {
            using var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddEntry(archive, ExeName, agentExe);
                AddEntry(archive, ConfigName, Encoding.UTF8.GetBytes(BuildConfigJson(options)));
                AddEntry(archive, InstallScript, Encoding.ASCII.GetBytes(BuildInstallScript()));
                AddEntry(archive, UninstallScript, Encoding.ASCII.GetBytes(BuildUninstallScript()));
            }

            return memory.ToArray();
        }

        private static void AddEntry(ZipArchive archive, string name, byte[] content)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(content, 0, content.Length);
        }

        // Batch scripts want CRLF line endings.
        private static string Join(params string[] lines) => string.Join("\r\n", lines) + "\r\n";
    }
}
