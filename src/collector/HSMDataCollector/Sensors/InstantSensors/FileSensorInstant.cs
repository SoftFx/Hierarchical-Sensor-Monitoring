using HSMDataCollector.Extensions;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSMDataCollector.Sensors
{
    internal sealed class FileSensorInstant : SensorInstant<List<byte>>, IFileSensor
    {
        private readonly FileSensorOptions _options;
        private readonly ILoggerManager _logger;


        public FileSensorInstant(FileSensorOptions options, ILoggerManager logger) : base(options)
        {
            _options = options;
            _logger = logger;
        }


        public void AddValue(string value, SensorStatus status, string comment) => AddValue(Encoding.UTF8.GetBytes(value).ToList(), status, comment);

        public void AddValue(string value, string comment) => AddValue(value, SensorStatus.Ok, comment);

        public void AddValue(string value) => AddValue(value, string.Empty);


        public async Task<bool> SendFile(string filePath, SensorStatus status = SensorStatus.Ok, string comment = "")
        {
            try
            {
                var fileInfo = new FileInfo(filePath);

                if (!fileInfo.Exists)
                {
                    _logger.Error($"{SensorPath} - {filePath} machine file not found!");
                    return false;
                }

                var fileName = Path.GetFileNameWithoutExtension(fileInfo.FullName);
                var extensions = fileInfo.Extension.TrimStart('.');

                using (var file = File.Open(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (var stream = new StreamReader(file))
                    {
                        var bytes = Encoding.UTF8.GetBytes(await stream.ReadToEndAsync()).ToList();
                        var value = ApplyCustomFileProperties(GetSensorValue(bytes), fileName, extensions).Complete(comment, status);

                        SendValue(value);
                    }
                }

                _logger.Info($"File: {filePath} has been send");
            }
            catch (Exception ex)
            {
                _logger.Error($"{SensorPath} - {ex.Message}");
                return false;
            }

            return true;
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