using System;
using System.IO;
using System.Text;
using System.Threading;

namespace HSMCommon
{
    public static class FileManager
    {
        private const int WaitTime = 5000;
        private const int MaxAttemptsCount = 5;


        public static void SafeCreateDirectory(string directoryPath)
        {
            void CreateDirectory() => Directory.CreateDirectory(directoryPath);

            DoActionWhileThereAreAttempts(CreateDirectory);
        }

        public static void SafeCopy(string path, string destination)
        {
            void CopyFile() => File.Copy(path, destination);

            DoActionWhileThereAreAttempts(CopyFile);
        }

        public static void SafeWriteToFile(string filePath, string text)
        {
            void WriteToFile()
            {
                using FileStream fs = new FileStream(filePath, FileMode.OpenOrCreate);
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                fs.Write(bytes, 0, bytes.Length);
            }

            DoActionWhileThereAreAttempts(WriteToFile);
        }

        public static void SafeRemoveFolder(string folderPath)
        {
            void RemoveFolder() => Directory.Delete(folderPath, true);

            DoActionWhileThereAreAttempts(RemoveFolder);
        }

        // Sync retry loop. Callers (test-fixture Dispose, static ctor) are synchronous and
        // fire-and-forget — they cannot await. The previous `async void` form routed the
        // terminal IOException through Task.ThrowAsync into the ThreadPool, which surfaces
        // as an unhandled exception and crashes the test-host process even when every test
        // has passed. Blocking the calling thread on Thread.Sleep is acceptable here: the
        // callers run at fixture disposal / startup, off any hot path.
        private static void DoActionWhileThereAreAttempts(Action action)
        {
            int attempts = 0;

            while (true)
            {
                try
                {
                    action?.Invoke();
                    return;
                }
                catch (IOException)
                {
                    if (++attempts == MaxAttemptsCount)
                        throw;

                    Thread.Sleep(WaitTime);
                }
            }
        }
    }
}
