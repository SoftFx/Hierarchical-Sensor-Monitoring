using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;

namespace CollectorTestApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private IDataCollector _dataCollector;

        public MainWindow()
        {
            InitializeComponent();

            textAddress.Text = "https://hsm.dev.soft-fx.eu/";
            textAccessKey.Text = "3394c79c-3c6d-4617-949a-ba8bc18b1878";
            textPort.Text = "44333";
            textModule.Text = "Main module";


            //InitializeCollector();



            Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if(_dataCollector == null)
                return;

            if (_dataCollector.Status == CollectorStatus.Running)
                _dataCollector.Stop();
        }

        private int GetPortNumber()
        {
            if(string.IsNullOrEmpty(textPort.Text))
            {
                throw new ApplicationException();
            }

            int portValue;

            if (int.TryParse(textPort.Text, out portValue))
                return portValue;

            return _defaultPort;
        }

        private void InitializeCollector()
        {
            var connectionOptions = new CollectorOptions()
            {
                AccessKey = textAccessKey.Text,
                ServerAddress = textAddress.Text,
                Port = GetPortNumber(), //default 44330
                Module = textModule.Text
            };

            _dataCollector = new DataCollector(connectionOptions);


            //var loggerOptions = new LoggerOptions()
            //{
            //    WriteDebug = true,
            //};

            //_dataCollector.AddNLog(loggerOptions);
            
            _dataCollector.Windows.AddAllDefaultSensors();
            _dataCollector.AddCustomLogger(new TextBoxLogger(paragraphLog));

           
            //_dataCollector.Windows.AddProcessMonitoringSensors()
            //    .AddSystemMonitoringSensors()
            //    .AddCollectorMonitoringSensors()
            //    .AddWindowsInfoMonitoringSensors()
            //    .AddAllDisksMonitoringSensors();


            //_dataCollector.Start();
        }

        private const int _defaultPort = 44330;

        private void textPort_TextChanged(object sender, TextChangedEventArgs e)
        {
            if(!int.TryParse(textPort.Text, out _))
            {
                textError.Text = $"Default port number: {_defaultPort}";
                return;
            }

            textError.Text = string.Empty;
        }

        private static void DisposeCollector(IDataCollector collector)
        {
            if (collector.Status == CollectorStatus.Running)
                collector.Stop();

            collector.Dispose();
        }

        private void buttonStart_Click(object sender, RoutedEventArgs e)
        {

            if (_dataCollector != null)
                DisposeCollector(_dataCollector);
            
            InitializeCollector();


            _dataCollector.Start();
            

            SetEnabled(true);
        }

        private void _dataCollector_ToStarting()
        {
            throw new NotImplementedException();
        }

        private void SetEnabled(bool isStarting)
        {
            buttonStart.IsEnabled = !isStarting;
            buttonStop.IsEnabled = isStarting;

            textAddress.IsEnabled = !isStarting;
            textAccessKey.IsEnabled = !isStarting;
            textModule.IsEnabled = !isStarting;
            textPort.IsEnabled = !isStarting;
            groupSensors.IsEnabled = isStarting;
        }

        private void buttonStop_Click(object sender, RoutedEventArgs e)
        {
            _dataCollector?.Stop();

            SetEnabled(false);
        }

        internal class TextBoxLogger : ICollectorLogger
        {
            private readonly Paragraph _paragraph;

            public TextBoxLogger(Paragraph paragraph)
            {
                _paragraph = paragraph;
            }

            public void Debug(string message)
            {
                _paragraph.Inlines.Add($"[{DateTime.Now}]\tDEBUG:\t{message}{Environment.NewLine}");
            }

            public void Info(string message)
            {
                _paragraph.Inlines.Add($"[{DateTime.Now}]\tINFO:\t{message}{Environment.NewLine}");
            }

            public void Error(string message)
            {
                _paragraph.Inlines.Add($"[{DateTime.Now}]\tERROR:\t{message}{Environment.NewLine}");
            }

            public void Error(Exception ex)
            {
                _paragraph.Inlines.Add($"[{DateTime.Now}]\tERROR:\t{ex}{Environment.NewLine}");
            }
        }


        private IInstantValueSensor<string> _textSensor;

        private void buttonSendText_Click(object sender, RoutedEventArgs e)
        {
            if (_textSensor == null)
                _textSensor = _dataCollector.CreateStringSensor(textSensorPath.Text);

            _textSensor.AddValue(textToSend.Text);
        }
    }


}