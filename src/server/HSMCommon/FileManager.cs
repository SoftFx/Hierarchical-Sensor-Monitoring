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

        // Synchronous retry loop. Callers (ServerConfig static ctor, DatabaseFixture
        // ctor, DatabaseRegisterFixture.Dispose) treat the operation as "done when I
        // return" — they cannot await, and several of them immediately write to / open
        // a database in the directory we just touched. Three failure modes had to be
        // avoided simultaneously:
        //
        //   1. `async void` routed the terminal IOException through Task.ThrowAsync
        //      into the ThreadPool and crashed the test-host process. The first attempt
        //      was synchronous, but the failure path was off-thread and un-catchable.
        //   2. `_ = Task.Run(...)` deferred even the first attempt, racing with
        //      immediate-successor writes in ServerConfig and with fresh-database
        //      opens in DatabaseFixture. It also faulted the discarded task on any
        //      non-IOException, turning programmer errors into UnobservedTaskException.
        //   3. `Thread.Sleep` + throw-after-max-retries cascaded into 310 xUnit
        //      TestClassExceptions, because fixture constructors re-threw the terminal
        //      IOException and xUnit wrapped it.
        //
        // The shape below makes the first attempt synchronous (so the common success
        // path completes before return), retries transient IOExceptions inline, and
        // swallows the terminal IOException so cleanup / setup does not propagate a
        // best-effort failure into callers that have no useful way to react. Non-IOException
        // errors (bad path, access denied) propagate to the caller deterministically —
        // those signal programmer error, not a transient race.
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
                        return;

                    Thread.Sleep(WaitTime);
                }
            }
        }
    }
}
