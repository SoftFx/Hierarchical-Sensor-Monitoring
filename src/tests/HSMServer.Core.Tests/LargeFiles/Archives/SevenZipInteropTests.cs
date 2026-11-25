using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using HSMServer.Core.LargeFiles.Archives;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles.Archives
{
    /// <summary>Runs when 7-Zip is installed (Windows: Program Files\7-Zip\7z.exe; Linux: 7z on PATH); skipped otherwise.</summary>
    public sealed class SevenZipFactAttribute : FactAttribute
    {
        public SevenZipFactAttribute()
        {
            if (SevenZip.Path is null)
                Skip = "7-Zip is not installed: C:\\Program Files\\7-Zip\\7z.exe on Windows, 7z on PATH on Linux";
        }
    }


    internal static class SevenZip
    {
        public static string Path { get; } = Find();


        private static string Find()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe");
                return File.Exists(path) ? path : null;
            }

            return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(folder => System.IO.Path.Combine(folder, "7z"))
                .FirstOrDefault(File.Exists);
        }

        public static (int ExitCode, string Output) Run(params string[] arguments)
        {
            var start = new ProcessStartInfo(Path)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);

            // The console of Windows has its own code page: ask 7-Zip for UTF-8, as it writes on Linux.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                start.ArgumentList.Add("-sccUTF-8");

            using var process = Process.Start(start)!;
            process.StandardInput.Close(); // a prompt for a password must not hang the test
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("7-Zip did not finish in 60 seconds");
            }

            return (process.ExitCode, output.Result + error.Result);
        }
    }


    // #1885: an archive the builder wrote opens in a third-party archiver with the password and only with it.
    public sealed class SevenZipInteropTests : IDisposable
    {
        private const string Password = "Seven-Zip-Interop-Pass-123";

        private readonly ArchiveBuilderHarness _h = new();


        public void Dispose() => _h.Dispose();


        [SevenZipFact]
        public async Task ArchiveOfTheBuilder_PassesSevenZipTestWithThePassword_AndFailsWithAWrongOne()
        {
            _h.Passwords.Set(_h.Product, Password);
            var file = _h.Store.AddVerified(_h.Product, TestContent.Compressible(2 * 1024 * 1024 + 7), name: "app.dmp");
            await _h.Run();

            var archive = _h.Store.ArchivePath(_h.Store.TryGetFile(file.FileId));

            var good = SevenZip.Run("t", $"-p{Password}", archive);
            Assert.True(good.ExitCode == 0, good.Output);
            Assert.Contains("Everything is Ok", good.Output);

            var listing = SevenZip.Run("l", "-slt", $"-p{Password}", archive);
            Assert.True(listing.ExitCode == 0, listing.Output);
            Assert.Contains("Path = app.dmp", listing.Output);
            Assert.Contains("AES-256", listing.Output);

            var wrong = SevenZip.Run("t", "-pwrong", archive);
            Assert.NotEqual(0, wrong.ExitCode);
        }

        [SevenZipFact]
        public async Task CyrillicEntryName_IsListedBySevenZipAsItWasSent()
        {
            const string name = "дамп процесса.dmp";

            _h.Passwords.Set(_h.Product, Password);
            var file = _h.Store.AddVerified(_h.Product, TestContent.Compressible(100_000), name: name);
            await _h.Run();

            var record = _h.Store.TryGetFile(file.FileId);
            Assert.Equal(LargeArchiveFileState.Packed, record.State);
            Assert.Equal(name, _h.OpenEntryName(file.FileId, Password));

            var archive = _h.Store.ArchivePath(record);
            var listing = SevenZip.Run("l", "-slt", $"-p{Password}", archive);
            Assert.True(listing.ExitCode == 0, listing.Output);
            Assert.Contains($"Path = {name}", listing.Output);

            var good = SevenZip.Run("t", $"-p{Password}", archive);
            Assert.True(good.ExitCode == 0, good.Output);
        }
    }
}
