using System;
using System.IO;
using System.Threading;

namespace HSMCommon
{
    public static class FileManager
    {
        const int _waitTime = 5000;
        public static void SafeDelete(string fileName)
        {
            //try to delete existing file. It can be locked.
            bool isDelete = false;
            int attempts = 5;

            while (!isDelete)
            {
                try
                {
                    File.Delete(fileName);
                    isDelete = true;
                }
                catch (IOException ex)
                {
                    attempts -= 1;

                    if (attempts == 0)
                        throw;

                    Thread.Sleep(_waitTime);
                }
            }
        }

        public static void SafeCreateDirectory(string directoryPath)
        {
            bool isCreate = false;
            int attempts = 5;

            while (!isCreate)
            {
                try
                {
                    Directory.CreateDirectory(directoryPath);
                    isCreate = true;
                }
                catch (Exception ex)
                {
                    attempts -= 1;

                    if (attempts == 0)
                        throw;

                    Thread.Sleep(_waitTime);
                }
            }
        }

        public static void SafeCreateFile(string filePath)
        {
            bool isCreate = false;
            int attempts = 5;

            while (!isCreate)
            {
                try
                {
                    File.Create(filePath);
                    isCreate = true;
                }
                catch (Exception ex)
                {
                    attempts -= 1;

                    if (attempts == 0)
                        throw;

                    Thread.Sleep(_waitTime);
                }
            }
        }

        public static void SafeWriteText(string filePath, string text)
        {
            bool isWrite = false;
            int attempts = 5;

            while (!isWrite)
            {
                try
                {
                    File.WriteAllText(filePath, text);
                    isWrite = true;
                }
                catch (Exception ex)
                {
                    attempts -= 1;

                    if (attempts == 0)
                        throw;

                    Thread.Sleep(_waitTime);
                }
            }
        }

        public static void SafeCopy(string path, string destination)
        {
            bool isWrite = false;
            int attempts = 5;

            while (!isWrite)
            {
                try
                {
                    File.Copy(path, destination);
                    isWrite = true;
                }
                catch (Exception ex)
                {
                    attempts -= 1;

                    if (attempts == 0)
                        throw;

                    Thread.Sleep(_waitTime);
                }
            }
        }
    }
}
