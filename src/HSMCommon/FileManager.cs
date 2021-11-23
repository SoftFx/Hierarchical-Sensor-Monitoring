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

        private static async void DoActionWhileThereAreAttempts(Action action)
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

                    await Task.Delay(WaitTime);
                }
            }
        }
    }
}
