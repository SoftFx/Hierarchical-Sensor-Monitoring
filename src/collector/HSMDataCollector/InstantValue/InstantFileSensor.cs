using HSMDataCollector.Base;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using System;
using System.Text;

namespace HSMDataCollector.InstantValue
{
    internal sealed class InstantFileSensor : SensorBase, IInstantValueSensor<string>
    {
        private readonly string _fileName;
        private readonly string _fileExtension;

        public override bool HasLastValue => false;


        public InstantFileSensor(string path, string productKey, string fileName, string extension, IValuesQueue queue, string description = "")
            : base(path, productKey, queue, description)
        {
            _fileName = fileName;
            _fileExtension = extension;
        }


        public override void Dispose() { }

        public override UnitedSensorValue GetLastValue()
        {
            throw new NotImplementedException();
        }

        public void AddValue(string value) =>
            EnqueueObject(CreateFile(value));

        public void AddValue(string value, string comment = "") =>
            EnqueueObject(CreateFile(value, comment));

        public void AddValue(string value, SensorStatus status = SensorStatus.Ok, string comment = "") =>
            EnqueueObject(CreateFile(value, comment, status));

        private FileSensorBytesValue CreateFile(string value, string comment = null, SensorStatus status = SensorStatus.Ok) =>
            new FileSensorBytesValue()
            {
                Key = ProductKey,
                Path = Path,
                Status = status,
                Time = DateTime.Now,
                Comment = comment,
                FileName = _fileName,
                Extension = _fileExtension,
                Value = Encoding.UTF8.GetBytes(value),
            };
    }
}
