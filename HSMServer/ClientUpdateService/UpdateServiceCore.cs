using System.Collections.Generic;
using System.IO;
using System.Linq;
using HSMServer.Configuration;
using HSMServer.Model;
using HSMServer.MonitoringServerCore;
using HSMService;
using Microsoft.Extensions.Logging;

namespace HSMServer.ClientUpdateService
{
    public class UpdateServiceCore : IUpdateService
    {
        private readonly ILogger<UpdateServiceCore> _logger;
        private string _updatePath;
        private ClientUpdateInfo _updateInfo;
        private List<string> _filesList;
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
            message.Files.AddRange(_filesList);
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

            byte[] bytes = File.ReadAllBytes(Path.Combine(_updatePath, fileName));
            return bytes;
        }


        private ClientUpdateInfo ReadUpdateInfo()
        {
            ClientUpdateInfo result = new ClientUpdateInfo();
            _filesList = Directory.GetFiles(_updatePath).ToList().Select(Path.GetFileName).ToList();
            result.FilesCount = _filesList.Count;
            result.Size = 0;
            return result;
        }
    }
}
