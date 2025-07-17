using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using NLog;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using ConnectionInfo = Renci.SshNet.ConnectionInfo;
using System.Threading.Tasks;


namespace HSMServer.Sftp
{
    public sealed class SftpWrapper : IDisposable
    {
        private  ConnectionInfo _connectionInfo;
        private readonly ILogger _logger;
        private string ConnectionMessage => $"Connecting to {Host}:{Port}, user={Username}...";
        private readonly SftpClient _client;

        public string     Host => _connectionInfo.Host;
        public int        Port => _connectionInfo.Port;
        public string Username => _connectionInfo.Username;
        public string RootPath { get; set; }





        public SftpWrapper(SftpConnectionConfig config, ILogger logger = null)
        {
            try
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

                _client = CreateClient();

                WrappedClientRequest(() => _client.Connect(), ConnectionMessage);

                RootPath = config.RootPath;
                _logger = logger;
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }

        private Task UploadFileInternalAsync(Stream stream, string path, bool canOverride = true, Action<ulong> uploadCallback = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var tcs = new TaskCompletionSource<SftpUploadAsyncResult>();

            var uploadResult = (SftpUploadAsyncResult)_client.BeginUploadFile(stream, path, canOverride, ar =>
            {
                var ar2 = (SftpUploadAsyncResult)ar;
                try
                {
                    _client.EndUploadFile(ar2);

                    if (ar2.IsUploadCanceled)
                    {
                        tcs.SetCanceled();
                    }
                    else if (ar2.IsCompleted)
                    {
                        tcs.SetResult(ar2);
                    }
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null, uploadCallback);

            cancellationToken.Register(() => uploadResult.IsUploadCanceled = true);
            return tcs.Task;
        }


        public async Task UploadFileAsync(string localFile, string sftpPath, bool canOverride = true, Action<ulong> uploadCallback = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var fileName = Path.GetFileName(localFile);
                var sftpFullName = Path.Combine(sftpPath, fileName).Replace('\\', '/');

                using (var stream = new FileStream(localFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    await WrappedClientRequestAsync(() => UploadFileInternalAsync(stream, sftpFullName, canOverride, uploadCallback, cancellationToken), $"Uploading file {localFile} to {sftpFullName}");
                }
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }

        public async Task UploadFileAsync(Stream stream, string path, bool canOverride = true, Action<ulong> uploadCallback = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                await WrappedClientRequestAsync(() => UploadFileInternalAsync(stream, path, canOverride, uploadCallback, cancellationToken), $"Uploading stream to {path}");
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
                sftpPath = sftpPath.Replace('\\', '/');
                result = WrappedClientRequest(() => _client.ListDirectory(sftpPath), $"Listing directory {sftpPath}...");
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }

            return result;
        }


        public async Task<bool> CheckWritePermissionsAsync(string sftpPath)
        {
            try
            {
                var fileName = Guid.NewGuid().ToString();
                var sftpFullName = Path.Combine(sftpPath, fileName).Replace('\\', '/');
                CreateAllDirectoriesInternal(sftpPath);
                using (var stream = new MemoryStream())
                {
                    await WrappedClientRequestAsync(() => UploadFileInternalAsync(stream, sftpFullName), $"Uploading stream to {sftpFullName}...");
                }
                WrappedClientRequest(() => _client.Delete(sftpFullName), $"deleting file {sftpFullName}...");
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }

            return true;
        }

        public async Task DeleteFileAsync(string sftpPath, CancellationToken cancellationToken = default)
        {
            try
            {
                await _client.DeleteFileAsync(sftpPath, cancellationToken);
            }   
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }

        public void CreateAllDirectories(string path)
        {
            try 
            {
                CreateAllDirectoriesInternal(path, false);
            }   
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }

        public void Dispose()
        {
            _client?.Disconnect();
            _client?.Dispose();
        }

        private void CreateAllDirectoriesInternal(string path, bool isFileName = false)
        {
            if (path.Length == 0)
                return;

            path = path.Replace('\\', '/');
            var arr = path.Split('/');

            int offset = isFileName ? 2 : 1;

            if (arr.Length == 1)
                offset = 1;

            string dir;

            for (int i = 0; i < arr.Length - offset; i++)
            {
                dir = arr[i];
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    if (!WrappedClientRequest(() => _client.Exists(dir), $"Checking directory {dir} exists..."))
                    {
                        WrappedClientRequest(() => _client.CreateDirectory(dir), $"Directory not exists. Creating {dir}...");
                    }
                    WrappedClientRequest(() => _client.ChangeDirectory(dir), $"Changing directory {dir}...");
                }
            }

            WrappedClientRequest(() => _client.ChangeDirectory("/"), "Changing directory to root...");
        }

        private void Init(string address, int? port, string user, string password, Stream privateKeyFile)
        {
            if (port.HasValue)
                _connectionInfo = new ConnectionInfo(address, port.Value, user, GetAuthMethods(user, password, privateKeyFile).ToArray());
            else
                _connectionInfo = new ConnectionInfo(address, user, GetAuthMethods(user, password, privateKeyFile).ToArray());
        }

        private SftpClient CreateClient() => WrappedClientRequest(() => new SftpClient(_connectionInfo), $"Creating sftp client {Host}:{Port}, username={Username}...");

        private async Task<T> WrappedClientRequestAsync<T>(Func<Task<T>> action, string message)
        {
            try
            {
                T result = await action.Invoke();
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

        private async Task WrappedClientRequestAsync(Func<Task> action, string message)
        {
            try
            {
                await action.Invoke();
                _logger?.Info(() => $"{message} successful");
            }
            catch (Exception ex)
            {
                _logger?.Error(() => message + " failed.");
                _logger?.Error(() => $"{ChangeExceptionMessage(ex.Message)}");
                throw;
            }
        }

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
