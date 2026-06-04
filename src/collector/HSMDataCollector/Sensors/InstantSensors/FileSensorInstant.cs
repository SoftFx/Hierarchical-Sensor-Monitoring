using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HSMDataCollector.Extensions;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.Sensors
{
    internal sealed class FileSensorInstant : SensorInstant<List<byte>, NoDisplayUnit>, IFileSensor
    {
        private readonly FileSensorOptions _options;
        private readonly ICollectorLogger _logger;


        public FileSensorInstant(FileSensorOptions options, ICollectorLogger logger) : base(options)
        {
            _options = options;
            _logger = logger;
        }


        public void AddValue(string value, SensorStatus status, string comment)
        {
            if (value == null)
                return;

            AddValue(Encoding.UTF8.GetBytes(value).ToList(), status, comment);
        }

        public void AddValue(string value, string comment) => AddValue(value, SensorStatus.Ok, comment);

        public void AddValue(string value) => AddValue(value, string.Empty);


        public async Task<bool> SendFile(string filePath, SensorStatus status = SensorStatus.Ok, string comment = "")
        {
            try
            {
                if (!SensorValueExtensions.IsValidStatus(status))
                    return false;

                if (!_dataProcessor.CanAcceptData)
                    return false;

                var fileInfo = new FileInfo(filePath);

                if (!fileInfo.Exists)
                {
                    _logger.Error($"{SensorPath} - {filePath} machine file not found!");
                    return false;
                }

                var fileName = Path.GetFileNameWithoutExtension(fileInfo.FullName);
                var extensions = fileInfo.Extension.TrimStart('.');

                if (_options.MaxFileSizeBytes <= 0)
                    throw new ArgumentOutOfRangeException(nameof(_options.MaxFileSizeBytes), "Max file size must be greater than zero.");

                if (fileInfo.Length > _options.MaxFileSizeBytes)
                {
                    _logger.Error($"{SensorPath} - {filePath} file size {fileInfo.Length} bytes exceeds limit {_options.MaxFileSizeBytes} bytes.");
                    return false;
                }

                if (fileInfo.Length > int.MaxValue)
                {
                    _logger.Error($"{SensorPath} - {filePath} file size {fileInfo.Length} bytes exceeds maximum supported size {int.MaxValue} bytes.");
                    return false;
                }

                var bytes = (await ReadAllBytesAsync(fileInfo).ConfigureAwait(false)).ToList();
                var value = ApplyCustomFileProperties(GetSensorValue(bytes), fileName, extensions).Complete(comment, status);

                SendValue(value);
                _logger.Info($"File: {filePath} has been send");
            }
            catch (Exception ex)
            {
                HandleException(ex);
                return false;
            }

            return true;
        }


        private static async Task<byte[]> ReadAllBytesAsync(FileInfo fileInfo)
        {
            using (var file = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, true))
            {
                var length = file.Length;
                if (length > int.MaxValue)
                    throw new IOException($"File is too large: {length} bytes.");

                var bytes = new byte[(int)length];
                int offset = 0;

                while (offset < bytes.Length)
                {
                    int read = await file.ReadAsync(bytes, offset, bytes.Length - offset).ConfigureAwait(false);
                    if (read == 0)
                        break;

                    offset += read;
                }

                if (offset == bytes.Length)
                    return bytes;

                Array.Resize(ref bytes, offset);
                return bytes;
            }
        }


        protected override SensorValueBase GetSensorValue(List<byte> value) => ApplyCustomFileProperties(base.GetSensorValue(value));


        private SensorValueBase ApplyCustomFileProperties(SensorValueBase value, string fileName = null, string extensions = null)
        {
            if (value is FileSensorValue fileValue)
            {
                fileValue.Name = fileName ?? _options.DefaultFileName;
                fileValue.Extension = extensions ?? _options.Extension;
            }

            return value;
        }
    }
}
