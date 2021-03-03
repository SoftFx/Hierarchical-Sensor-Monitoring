using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace HSMClientUpdater
{
    class Program
    {
        private static Mutex _mutex;
        private static string _updaterExeFileName;
        private static string _clientExeFileName;
        private static string _appName;
        private static string _appDirectory;
        private static string _updateDirectory;
        private static string _clientAppName;
        const int _waitTime = 5000;
        private static List<string> _notTouchedDirectories = new List<string> { "Config", "Logs" };
        private static List<string> _skipFiles = new List<string> { "HSMClientUpdater.dll"};
        static void Main(string[] args)
        {
            _appName = "HSMClientUpdater";
            Console.WriteLine($"HSMClient updater started at {DateTime.Now:G}");
            ParseArgs(args);
            _skipFiles.Add(_updaterExeFileName);
            if (IsRunningAlready())
            {
                Console.WriteLine($"Updater is already running!{Environment.NewLine}Shutting down...");
                Console.WriteLine("Press enter to continue");
                Console.ReadLine();
                return;
            }

            if (IsClientRunning())
            {
                Console.WriteLine($"Client app is already running!{Environment.NewLine}Shutting down...");
                Console.WriteLine("Press enter to continue");
                Console.ReadLine();
                return;
            }

            RunUpdate();
            Console.WriteLine($"Update finished at {DateTime.Now:G}");
        }


        #region Apps Checking
        private static bool IsClientRunning()
        {
            try
            {
                Mutex.OpenExisting(GetClientMutexName());
                return true;
            }
            catch (WaitHandleCannotBeOpenedException e)
            {
                return false;
            }
        }
        private static bool IsRunningAlready()
        {
            bool exists = false;
            try
            {
                _mutex = new Mutex(true, GetUpdaterMutexName(), out exists);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            return !exists;
        }
        private static string GetUpdaterMutexName()
        {
            return $"{_appName}_{_appDirectory.GetHashCode()}";
        }

        private static string GetClientMutexName()
        {
            return _clientAppName;
        }

        #endregion

        #region Close client

        private static void CloseClient(string clientFile)
        {
            Console.WriteLine("Wait for HSMClient to close");

            Process clientProcess = GetClientProcess(clientFile);
            if (clientProcess != null)
            {
                clientProcess.WaitForExit(10000);
            }
            else
                return;

            Console.WriteLine("Killing HSMClient process");
            if (!clientProcess.HasExited)
            {
                clientProcess.Kill();
                clientProcess.WaitForExit();
            }

        }

        private static Process GetClientProcess(string clientFile)
        {
            Process[] processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(clientFile));
            if (processes.Length == 0)
                return null;

            foreach (Process process in processes)
            {
                if (string.Equals(clientFile, process.MainModule.FileName, StringComparison.InvariantCultureIgnoreCase))
                    return process;
            }

            return null;
        }

        #endregion

        #region Update

        private static void RunUpdate()
        {
            Console.WriteLine($"Starting client update at {DateTime.Now:G}");
            try
            {
                string clientFile = Path.Combine(_appDirectory, _clientExeFileName);
                CloseClient(clientFile);

                Console.WriteLine("Replacing app files");
                //DoEvents();

                bool updated = ReplaceFiles(_appDirectory);
                if (updated)
                {
                    Console.WriteLine($"Client files updated at {DateTime.Now:G}");
                }

                Console.WriteLine("Starting HSMClient");

                //DoEvents();

                try
                {
                    Process.Start(clientFile);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to start client: {e}");
                    Console.WriteLine("Press enter to continue");
                    Console.ReadLine();
                }

            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e}");
                Console.WriteLine("Press enter to continue");
                Console.ReadLine();

            }
        }
        private static bool ReplaceFiles(string appDirectory)
        {
            try
            {
                if (!Directory.Exists(_updateDirectory))
                    return false;

                string[] updateFiles = Directory.GetFiles(_updateDirectory);
                if (!updateFiles.Any())
                    return false;

                string oldFolder = Path.Combine(appDirectory, "Old");
                if (!Directory.Exists(oldFolder))
                    Directory.CreateDirectory(oldFolder);

                Console.WriteLine($"Update files will be moved from {_updateDirectory} to {appDirectory}");
                CopyFolder(_updateDirectory, appDirectory);

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Copy files error: {e}");
                return false;
            }
        }

        private static void CopyFolder(string sourceFolder, string destinationFolder)
        {
            Console.WriteLine($"Will move {sourceFolder}/ directory to {destinationFolder}/");
            foreach (string directory in Directory.GetDirectories(sourceFolder))
            {
                if (directory == destinationFolder)
                    continue;

                string folderName = Path.GetDirectoryName(directory);

                if (string.IsNullOrEmpty(folderName))
                    continue;

                if (_notTouchedDirectories.Contains(folderName.ToLower()))
                    continue;

                string newDirectory = Path.Combine(destinationFolder, folderName);

                if (!Directory.Exists(newDirectory))
                    Directory.CreateDirectory(newDirectory);

                CopyFolder(directory, newDirectory);
            }

            CopyFiles(sourceFolder, destinationFolder);
        }

        private static void CopyFiles(string source, string destination)
        {
            string[] sourceFiles = Directory.GetFiles(source);
            Console.WriteLine($"About to move {sourceFiles.Length} files from {source} to {destination}");
            int count = sourceFiles.Length;
            for (int i = 0; i < count; ++i)
            {
                string fileName = Path.GetFileName(sourceFiles[i]);
                if (string.IsNullOrEmpty(fileName))
                    continue;

                if (_skipFiles.Contains(fileName))
                    continue;

                string targetFile = Path.Combine(destination, fileName);

                Console.WriteLine($"Moving {sourceFiles[i]} to {targetFile}...");
                //if (File.Exists(targetFile))
                //    File.Delete(targetFile);

                SafeMove(sourceFiles[i], targetFile, true);
                Console.WriteLine($"{i+1}/{count} files moved successfully");
            }
        }

        private static void SafeMove(string source, string destination, bool overWrite)
        {
            bool isWrite = false;
            int attempts = 5;

            while (!isWrite)
            {
                try
                {
                    File.Move(source, destination, overWrite);
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
        #endregion
        private static void ParseArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i = i + 2)
            {
                switch (args[i])
                {
                    case "-client":
                    {
                        _clientAppName = args[i + 1];
                        break;
                    }
                    case "-upd":
                    {
                        _updaterExeFileName = args[i + 1];
                        break;
                    }
                    case "-clientExe":
                    {
                        _clientExeFileName = args[i + 1];
                        break;
                    }
                    case "-appDir":
                    {
                        _appDirectory = args[i + 1];
                        break;
                    }
                    case "-updDir":
                    {
                        _updateDirectory = args[i + 1];
                        break;
                    }

                }
            }
        }
    }
}
