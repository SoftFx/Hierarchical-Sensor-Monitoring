using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.ViewModel;
using HSMSensorDataObjects.TypedDataObject;

namespace HSMClient.Dialog
{
    public class ClientFileSensorModel : ClientDialogModelBase
    {
        private readonly string _filesFolderName = "Files";
        private MonitoringSensorViewModel _viewModel;
        private string _folderPath;
        public ClientFileSensorModel(ISensorHistoryConnector connector, MonitoringSensorViewModel sensor) : base(connector, sensor)
        {
            _folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _filesFolderName);
            _viewModel = sensor;
            ExpandValue();
        }

        private void ExpandValue()
        {
            CheckFilesFolder();
            string fileNameWithoutExtension =
                $"{_viewModel.Product}_{_viewModel.Path}_{_viewModel.SensorUpdate.Time.Ticks}".Replace("/", "_");
            string filePath = Path.Combine(_folderPath, fileNameWithoutExtension);
            string existingFile = CheckFileExistence(fileNameWithoutExtension);

            if (string.IsNullOrEmpty(existingFile))
            {
                var items = _connector.GetSensorHistory(_product, _path, _name, -1);
                if (items.Count < 1)
                    return;

                var item = items[0];
                var typedData = JsonSerializer.Deserialize<FileSensorData>(item.SensorValue);

                filePath = $"{filePath}.{typedData.Extension}";
                File.WriteAllText(filePath, typedData.FileContent);
            }
            else
            {
                filePath = existingFile;
            }
            
            Process process = new Process() {StartInfo = new ProcessStartInfo(filePath) {UseShellExecute = true}};
            process.Start();
        }

        private void CheckFilesFolder()
        {
            if (!Directory.Exists(_folderPath))
            {
                Directory.CreateDirectory(_folderPath);
            }
        }

        private string CheckFileExistence(string file)
        {
            var files = Directory.GetFiles(_folderPath);
            foreach (var existingFile in files)
            {
                var fileNoExtension = Path.GetFileNameWithoutExtension(existingFile);
                if (file == fileNoExtension)
                {
                    return existingFile;
                }
            }

            return string.Empty;
        }
    }
}
