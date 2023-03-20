using HSMDataCollector.Base;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Linq;
using System.Text;

namespace HSMDataCollector.InstantValue
{
    internal sealed class InstantFileSensor : SensorBase, IInstantValueSensor<string>
    {
        private readonly string _fileName;
        private readonly string _fileExtension;

        public override bool HasLastValue => false;


        public InstantFileSensor(string path, string fileName, string extension, IValuesQueue queue, string description = "")
            : base(path, queue, description)
        {
            _fileName = fileName;
            _fileExtension = extension;
        }
  

        public override void Dispose() { }

        public override SensorValueBase GetLastValue()
        {
            throw new NotImplementedException();
        }

        public void AddValue(string value) =>
            EnqueueValue(CreateFile(value));

        public void AddValue(string value, string comment = "") =>
            EnqueueValue(CreateFile(value, comment));

        public void AddValue(string value, SensorStatus status = SensorStatus.Ok, string comment = "") =>
            EnqueueValue(CreateFile(value, comment, status));

        private FileSensorValue CreateFile(string value, string comment = null, SensorStatus status = SensorStatus.Ok) =>
            new FileSensorValue()
            {
                Path = Path,
                Status = status,
                Time = DateTime.Now,
                Comment = comment,
                Name = _fileName,
                Extension = _fileExtension,
                Value = Encoding.UTF8.GetBytes(value).ToList(),
            };
    }
}
