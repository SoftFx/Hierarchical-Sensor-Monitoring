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
using System.Windows.Threading;
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
        private const int _defaultPort = 44330;

        public MainWindow()
        {
            InitializeComponent();

            //textAddress.Text = "https://localhost/";
            //textAccessKey.Text = "3394c79c-3c6d-4617-949a-ba8bc18b1878";

            textAccessKey.Text = string.IsNullOrEmpty(Properties.Settings.Default.ProductID) ? "d5550f7b-c480-4d1a-8788-3f1e72452214" : Properties.Settings.Default.ProductID;
            textPort.Text = string.IsNullOrEmpty(Properties.Settings.Default.Port) ? "44333" : Properties.Settings.Default.Port;
            textAddress.Text = string.IsNullOrEmpty(Properties.Settings.Default.ServerAddress) ? "https://localhost/" : Properties.Settings.Default.ServerAddress;
            textModule.Text = "Main module";

            textLog.TextChanged += (a, b) => textLog.ScrollToEnd();
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {


            if(_dataCollector == null)
                return;

            if (_dataCollector.Status == CollectorStatus.Running)
                _dataCollector.Stop();
        }

        private void SetWaitingMode(bool waiting)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(new Action<bool>(SetWaitingMode), waiting);
                return;
            }

            panelFog.SetValue(Panel.ZIndexProperty, waiting ? 1 : -1);
            panelWaitingImage.SetValue(Panel.ZIndexProperty, waiting ? 2 : -1);
            panelWaitingImage.Visibility = waiting ? Visibility.Visible : Visibility.Hidden;
            panelFog.Visibility = waiting ? Visibility.Visible : Visibility.Hidden;
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

            SetWaitingMode(true);

            _dataCollector.ToRunning += () => 
            {
                SetWaitingMode(false);
            };

            _dataCollector.Start();

            try
            {
                SaveSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Settings saving error: {ex.Message}");
            }

            SetEnabled(true);

           

        }


        private void SaveSettings()
        {
            Properties.Settings.Default.ProductID = textAccessKey.Text;
            Properties.Settings.Default.Port = textPort.Text;
            Properties.Settings.Default.ServerAddress = textAddress.Text;
            Properties.Settings.Default.Save();
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
            SetWaitingMode(true);

            _dataCollector.ToStopped += () =>
            {
                SetWaitingMode(false);
            };

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
                if (!_paragraph.Dispatcher.CheckAccess())
                {
                    _paragraph.Dispatcher.Invoke(new Action<string>(Debug), message);
                    return;
                }
                _paragraph.Inlines.Add($"[{DateTime.Now}]\tDEBUG:\t{message}{Environment.NewLine}");
            }

            public void Info(string message)
            {
                if (!_paragraph.Dispatcher.CheckAccess())
                {
                    _paragraph.Dispatcher.Invoke(new Action<string>(Info), message);
                    return;
                }
                _paragraph.Inlines.Add(new Run($"[{DateTime.Now}]\tINFO:\t{message}{Environment.NewLine}") { Foreground = Brushes.RoyalBlue });
            }

            public void Error(string message)
            {
                if (!_paragraph.Dispatcher.CheckAccess())
                {
                    _paragraph.Dispatcher.Invoke(new Action<string>(Error), message);
                    return;
                }
                _paragraph.Inlines.Add(new Run($"[{DateTime.Now}]\tERROR:\t{message}{Environment.NewLine}") { Foreground = Brushes.IndianRed});
            }

            public void Error(Exception ex)
            {
                if (!_paragraph.Dispatcher.CheckAccess())
                {
                    _paragraph.Dispatcher.Invoke(new Action<Exception>(Error), ex);
                    return;
                }
                _paragraph.Inlines.Add(new Run($"[{DateTime.Now}]\tERROR:\t{ex}{Environment.NewLine}") { Foreground = Brushes.IndianRed });
            }
        }


        private IInstantValueSensor<string> _textSensor;
        private IMonitoringRateSensor _rateSensor;
        private IMonitoringRateSensor _rateM1Sensor;
        private IMonitoringRateSensor _rateM5Sensor;
        private DispatcherTimer _timer;
        private Random _random;

        private void buttonSendText_Click(object sender, RoutedEventArgs e)
        {
            if (_textSensor == null)
                _textSensor = _dataCollector.CreateStringSensor(textSensorPath.Text);

            _textSensor.AddValue(textToSend.Text);
        }

        private void buttonStartRate_Click(object sender, RoutedEventArgs e)
        {
            if (_timer != null)
                return;


            _random = new Random();
            _rateSensor = _dataCollector.CreateRateSensor("PerSecTest");

            _rateM1Sensor = _dataCollector.CreateM1RateSensor("PerSecM1Test");
            _rateM5Sensor = _dataCollector.CreateM5RateSensor("PerSecM5Test");

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (s, e) =>
            {
                var value = _random.NextDouble() * 5;

                _rateSensor.AddValue(value);
                _rateM1Sensor.AddValue(value);
                _rateM5Sensor.AddValue(value);

                buttonStartRate.Content = value.ToString();
            };
            _timer.Start();

            buttonStartRate.IsEnabled = false;
            buttonStopRate.IsEnabled = true;
        }

        private void buttonStopRate_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();
            _timer = null;

            buttonStartRate.IsEnabled = true;
            buttonStartRate.Content = "Start send rate";
            buttonStopRate.IsEnabled = false;
            
        }


    }


}