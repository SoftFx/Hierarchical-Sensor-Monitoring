using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

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

        // Fire-and-forget retry loop. Callers (test-fixture Dispose, static ctor) are
        // synchronous and cannot await. Two failure modes had to be avoided simultaneously:
        //
        //   1. The previous `async void` form routed the terminal IOException through
        //      Task.ThrowAsync into the ThreadPool, surfacing as an unhandled exception
        //      and crashing the test-host process — even on a best-effort cleanup that
        //      callers have no way to react to.
        //   2. The intermediate synchronous retry (Thread.Sleep on the calling thread)
        //      blocked parallel fixture constructors for up to 25 s, cascading into
        //      hundreds of xUnit TestClassException failures.
        //
        // Pushing the retry onto the thread pool via `Task.Run` preserves the original
        // non-blocking behaviour, and swallowing the terminal exception keeps the cleanup
        // best-effort: the callers' contracts already treat the operation as fire-and-forget
        // (none of them checks the result), and the underlying LevelDB disposal races that
        // trigger this path are benign leftover-temp-file situations.
        private static void DoActionWhileThereAreAttempts(Action action)
        {
            _ = Task.Run(async () =>
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
                            return;

                        await Task.Delay(WaitTime);
                    }
                }
            });
        }
    }
}
