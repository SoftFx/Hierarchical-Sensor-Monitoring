using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using System;

namespace HSMDataCollector.CustomFuncSensor
{
    internal sealed class FileFuncSensor : CustomFuncSensorBase<FileSensorBytesValue>, INoParamsFuncSensor<byte[]>
    {
        private readonly Func<byte[]> _funcToInvoke;
        private readonly string _fileName;
        private readonly string _fileExtension;
        private readonly NLog.Logger _logger;

        public override bool HasLastValue => false;


        public FileFuncSensor(string path, string productKey, string fileName, string extension, IValuesQueue queue,
            string description, TimeSpan timerSpan, Func<byte[]> funcToInvoke, bool isLogging)
            : base(path, productKey, queue, description, timerSpan, SensorType.FileSensorBytes)
        {
            _funcToInvoke = funcToInvoke;
            _fileName = fileName;
            _fileExtension = extension;

            if (isLogging)
                _logger = Logger.Create(nameof(FileFuncSensor));
        }

        public Func<byte[]> GetFunc() => _funcToInvoke;

        public TimeSpan GetInterval() => TimerSpan;

        public void RestartTimer(TimeSpan timeSpan) => RestartTimerInternal(timeSpan);

        public override UnitedSensorValue GetLastValue()
        {
            throw new NotImplementedException();
        }

        protected override FileSensorBytesValue GetInvokeResult()
        {
            try
            {
                return CreateFile(_funcToInvoke.Invoke(), SensorStatus.Ok);
            }
            catch (Exception e)
            {
                _logger?.Error(e);

                return CreateFile(Array.Empty<byte>(), SensorStatus.Error);
            }
        }

        private FileSensorBytesValue CreateFile(byte[] value, SensorStatus status) =>
            new FileSensorBytesValue()
            {
                Key = ProductKey,
                Path = Path,
                Status = status,
                Time = DateTime.Now,
                FileName = _fileName,
                Extension = _fileExtension,
                Value = value,
            };
    }
}
