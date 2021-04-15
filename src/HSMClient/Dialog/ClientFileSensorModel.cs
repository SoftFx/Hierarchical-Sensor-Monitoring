using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.ViewModel;
using HSMSensorDataObjects.TypedDataObject;

namespace HSMClient.Dialog
{
    public class ClientFileSensorModel : ClientDialogModelBase
    {
        private readonly string _filesFolderName = "Files";
        public ClientFileSensorModel(ISensorHistoryConnector connector, MonitoringSensorViewModel sensor) : base(connector, sensor)
        {
            ExpandValue();
        }

        private void ExpandValue()
        {
            var items = _connector.GetSensorHistory(_product, _path, _name, -1);
            if (items.Count < 1)
                return;

            var item = items[0];
            var typedData = JsonSerializer.Deserialize<FileSensorData>(item.SensorValue);
            CheckFilesFolder();
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _filesFolderName,
                $"{DateTime.Now.Ticks}.{typedData.Extension}");
            File.WriteAllText(filePath, typedData.FileContent);
            Process process = new Process() {StartInfo = new ProcessStartInfo(filePath) {UseShellExecute = true}};
            process.Start();
        }

        private void CheckFilesFolder()
        {
            string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _filesFolderName);
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
        }
    }
}
