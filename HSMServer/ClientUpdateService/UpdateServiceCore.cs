using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AdminService;
using HSMServer.Configuration;
using HSMServer.Model;
using Microsoft.Extensions.Logging;
using NLog;

namespace HSMServer.ClientUpdateService
{
    public class UpdateServiceCore : IUpdateService
    {
        private readonly ILogger<UpdateServiceCore> _logger;
        private string _updatePath;
        private ClientUpdateInfo _updateInfo;
        private string[] _filesList;
        public UpdateServiceCore(ILogger<UpdateServiceCore> logger)
        {
            _logger = logger;
            _updatePath = Config.ClientAppFolderPath;
            _updateInfo = ReadUpdateInfo();
            _logger.LogInformation("Update uploader initialized");
        }

        public UpdateInfoMessage GetUpdateInfo()
        {
            UpdateInfoMessage message = new UpdateInfoMessage();
            message.Size = _updateInfo.Size;
            message.FilesCount = _updateInfo.FilesCount;
            return message;
        }

        public byte[] GetUpdateFile(int index)
        {
            string file = _filesList[index];
            byte[] fileBytes = File.ReadAllBytes(file);
            return fileBytes;
        }

        public byte[] GetFileContents(string fileName)
        {
            if (!_filesList.Contains(fileName))
            {
                return new byte[1];
            }

            byte[] bytes = File.ReadAllBytes(fileName);
            return bytes;
        }


        private ClientUpdateInfo ReadUpdateInfo()
        {
            ClientUpdateInfo result = new ClientUpdateInfo();
            _filesList = Directory.GetFiles(_updatePath);
            result.FilesCount = _filesList.Length;
            double size = _filesList.Aggregate<string, double>(0, (current, file) => current + new FileInfo(file).Length);
            result.Size = size;
            return result;
        }
    }
}
