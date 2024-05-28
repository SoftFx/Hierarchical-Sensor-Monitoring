using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using NLog;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using ConnectionInfo = Renci.SshNet.ConnectionInfo;


namespace HSMServer.Sftp
{
    public sealed class SftpWrapper
    {
        private  ConnectionInfo _connectionInfo;
        private readonly ILogger _logger;
        private string _connectionMessage => $"Connecting to {Host}:{Port}, user={Username}...";

        public string     Host => _connectionInfo.Host;
        public int        Port => _connectionInfo.Port;
        public string Username => _connectionInfo.Username;
        public string RootPath { get; set; }


        public SftpWrapper(SftpConnectionConfig config, ILogger logger = null)
        {
            if (string.IsNullOrEmpty(config.PrivateKey))
            {
                 Init(config.Address, config.Port, config.Username, config.Password, null);
             }
            else
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(config.PrivateKey)))
                {
                    Init(config.Address, config.Port, config.Username, config.Password, stream);
                }
            }

            RootPath = config.RootPath;
            _logger = logger;
        }


        public bool CheckConnection()
        {
            bool result = false;
            try
            {
                using (var client = CreateClient())
                {
                    WrappedClientRequest(() => client.Connect(), _connectionMessage);
                    try
                    {
                        result = client.IsConnected;
                    }
                    finally
                    {
                        WrappedClientRequest(() => client.Disconnect(), "Disconnecting...");
                    }
                }
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }

            return result;
        }

        public void UploadFile(string localFile, string sftpPath, CancellationToken token)
        {
            try
            {
                UploadFile(localFile, sftpPath, (result) =>
                {
                    if (result is SftpUploadAsyncResult uploadResult && !uploadResult.IsUploadCanceled)
                    {
                        uploadResult.IsUploadCanceled = token.IsCancellationRequested;
                    }
                });
                token.ThrowIfCancellationRequested();
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }

        public void UploadFile(Stream stream, string fileName, string sftpPath, CancellationToken token)
        {
            try
            {
                UploadFile(stream, fileName, sftpPath, (result) =>
                {
                    if (result is SftpUploadAsyncResult uploadResult && !uploadResult.IsUploadCanceled)
                    {
                        uploadResult.IsUploadCanceled = token.IsCancellationRequested;
                    }
                });
                token.ThrowIfCancellationRequested();
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }

        public void UploadFile(string localFile, string sftpPath, AsyncCallback asyncCallback)
        {
            try
            {
                if (!File.Exists(localFile))
                    throw new FileNotFoundException();
                var fileName = Path.GetFileName(localFile);
                var sftpFullName = Path.Combine(sftpPath, fileName).Replace('\\', '/');
                using (var client = CreateClient())
                {
                    WrappedClientRequest(() => client.Connect(), _connectionMessage);
                    try
                    {
                        CreateAllDirectoriesInternal(client, sftpPath);
                        using (var stream = new FileStream(localFile, FileMode.Open, FileAccess.Read))
                        {
                            SftpUploadAsyncResult result = null;
                            WrappedClientRequest(() => { result = client.BeginUploadFile(stream, sftpFullName, asyncCallback) as SftpUploadAsyncResult; }, $"Starting upload file {sftpFullName}...");
                            WrappedClientRequest(() => client.EndUploadFile(result), "Ending upload file...");
                        }
                    }
                    finally
                    {
                        WrappedClientRequest(() => client.Disconnect(), "Disconnecting...");
                    }
                }
            }
            catch (Exception ex)
            {
               HandleException(ex);
            }
        }

        public void UploadFile(Stream stream, string fileName, string sftpPath, AsyncCallback asyncCallback)
        {
            try
            {
                var sftpFullName = Path.Combine(sftpPath, fileName).Replace('\\', '/');
                using (var client = CreateClient())
                {
                    WrappedClientRequest(() => client.Connect(), _connectionMessage);
                    try
                    {
                        CreateAllDirectoriesInternal(client, sftpPath);
                        SftpUploadAsyncResult result = null;
                        WrappedClientRequest(() => { result = client.BeginUploadFile(stream, sftpFullName, asyncCallback) as SftpUploadAsyncResult; }, $"Starting upload file {sftpFullName}...");
                        WrappedClientRequest(() => client.EndUploadFile(result), "Ending upload file...");
                    }
                    finally
                    {
                        WrappedClientRequest(() => client.Disconnect(), "Disconnecting...");
                    }
                }
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }

        public IEnumerable<ISftpFile> ListDirectory(string sftpPath)
        {
            IEnumerable<ISftpFile> result = null;
            try
            {
                using (var client = CreateClient())
                {
                    WrappedClientRequest(() => client.Connect(), _connectionMessage);
                    try
                    {
                        sftpPath = sftpPath.Replace('\\', '/');
                        result = WrappedClientRequest(() => client.ListDirectory(sftpPath), $"Listing directory {sftpPath}...");
                    }
                    finally
                    {
                        WrappedClientRequest(() => client.Disconnect(), "Disconnecting...");
                    }
                }
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }

            return result;
        }

        public bool CheckReadPermissions(string sftpPath)
        {
            try
            {
                using (var client = CreateClient())
                {
                    WrappedClientRequest(() => client.Connect(), _connectionMessage);
                    try
                    {
                        sftpPath = sftpPath.Replace('\\', '/');
                        WrappedClientRequest(() => client.ListDirectory(sftpPath), $"Listing directory {sftpPath}...");
                    }
                    finally
                    {
                        WrappedClientRequest(() => client.Disconnect(), "Disconnecting...");
                    }
                }
            }
            catch (Exception ex)
            {
               HandleException(ex);
            }

            return true;
        }

        public bool CheckWritePermissions(string sftpPath)
        {
            try
            {
                var fileName = Guid.NewGuid().ToString();
                var sftpFullName = Path.Combine(sftpPath, fileName).Replace('\\', '/');
                using (var client = CreateClient())
                {
                    WrappedClientRequest(() => client.Connect(), _connectionMessage);
                    try
                    {
                        CreateAllDirectoriesInternal(client, sftpPath.Replace('\\', '/'));
                        using (var stream = new MemoryStream())
                        {
                            SftpUploadAsyncResult result = null;
                            WrappedClientRequest(() => { result = client.BeginUploadFile(stream, sftpFullName, null) as SftpUploadAsyncResult; }, $"Starting upload file {sftpFullName}...");
                            WrappedClientRequest(() => client.EndUploadFile(result), "Ending upload file...");
                        }
                        WrappedClientRequest(() => client.Delete(sftpFullName), $"deleting file {sftpFullName}...");
                    }
                    finally
                    {
                        WrappedClientRequest(() => client.Disconnect(), "Disconnecting...");
                    }
                }
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }

            return true;
        }

        private void CreateAllDirectoriesInternal(SftpClient client, string path)
        {
            path = path.Replace('\\', '/');
            foreach (string dir in path.Split('/'))
            {
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    if (!WrappedClientRequest(() => client.Exists(dir), $"Checking directory {dir} exists..."))
                    {
                        WrappedClientRequest(() => client.CreateDirectory(dir), $"Directory not exists. Creating {dir}...");
                    }
                    WrappedClientRequest(() => client.ChangeDirectory(dir), $"Changing directory {dir}...");
                }
            }

            WrappedClientRequest(() => client.ChangeDirectory("/"), "Changing directory to root...");
        }

        private void Init(string address, int? port, string user, string password, Stream privateKeyFile)
        {
            try
            {
                if (port.HasValue)
                    _connectionInfo = new ConnectionInfo(address, port.Value, user, GetAuthMethods(user, password, privateKeyFile).ToArray());
                else
                    _connectionInfo = new ConnectionInfo(address, user, GetAuthMethods(user, password, privateKeyFile).ToArray());
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }

        private SftpClient CreateClient() => WrappedClientRequest(() => new SftpClient(_connectionInfo), $"Creating sftp client {Host}:{Port}, username={Username}...");

        private T WrappedClientRequest<T>(Func<T> action, string message)
        {
            try
            {
                T result = action.Invoke();
                _logger?.Info(() => $"{message} successful");
                return result;
            }
            catch (Exception ex)
            {
                _logger?.Error(() => message + " failed.");
                _logger?.Error(() => $"{ChangeExceptionMessage(ex.Message)}");
                throw;
            }
        }

        private void WrappedClientRequest(Action action, string message)
        {
            try
            {
                action.Invoke();
                _logger?.Info(() => $"{message} successful");
            }
            catch (Exception ex)
            {
                _logger?.Error(() => message + " failed.");
                _logger?.Error(() => $"{ChangeExceptionMessage(ex.Message)}");
                throw;
            }
        }

        private static void HandleException(Exception ex)
        {
            var message = ChangeExceptionMessage(ex.Message);

            if (message == "[Unable to find the specified file.]")
                throw new FileNotFoundException(message);

            if (message != ex.Message)
                throw new Exception(message);

            throw ex;
        }

        private static string ChangeExceptionMessage(string message)
        {
            if (message == "Invalid data type, INTEGER(02) is expected." || message.Contains("DER length is"))
                return $"Invalid private key or passphrase. [{message}]";

            if (message.Contains("No such file"))
                return $"Invalid working directory or no permissions. [{message}]";

            return $"{message}";
        }

        private static List<AuthenticationMethod> GetAuthMethods(string user, string password, Stream privateKeyFile)
        {
            var methods = new List<AuthenticationMethod>();
            if (privateKeyFile != null)
            {
                PrivateKeyFile[] keyFiles;
                if (!string.IsNullOrEmpty(password))
                {
                    keyFiles = new[] { new PrivateKeyFile(privateKeyFile, password) };
                }
                else
                {
                    keyFiles = new[] { new PrivateKeyFile(privateKeyFile) };
                }
                methods.Add(new PrivateKeyAuthenticationMethod(user, keyFiles));
            }
            if (!string.IsNullOrEmpty(password))
            {
                methods.Add(new PasswordAuthenticationMethod(user, password));
            }

            return methods;
        }
    }
}
