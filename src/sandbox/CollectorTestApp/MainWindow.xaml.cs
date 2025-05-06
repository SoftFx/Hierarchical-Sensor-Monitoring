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

            string firstID = textAccessKey.Text.Split(new char[] { '-' })[0];
            Title = $"HSM text: {firstID}";
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

        private void buttonSendText_Click(object sender, RoutedEventArgs e)
        {
            if (_textSensor == null)
                _textSensor = _dataCollector.CreateStringSensor(textSensorPath.Text);

            _textSensor.AddValue(textToSend.Text);
        }

        DispatcherTimer? _timer;

        private void buttonAutoSend_Click(object sender, RoutedEventArgs e)
        {
            CreateSensors( (int)sliderCount.Value);

            if (buttonAutoSend.IsChecked ?? false)
            {
                _timer = new DispatcherTimer();

                double millisec = sliderInterval.Value * 1000;
                _timer.Interval = TimeSpan.FromMilliseconds(millisec);
                _timer.Tick += timer_Tick;

                _timer.Start();
            }
            else
            {
                _timer?.Stop();
                _timer = null;
                buttonAutoSentText.Text = string.Empty;
            }
        }

        List<IInstantValueSensor<string>> _textSensors = new List<IInstantValueSensor<string>>();



        private void CreateSensors(int count)
        {
            _textSensors.Clear();

            for (int i = 0; i < count; i++)
            {
                string sensorName = $"{textSensorPath.Text} ({i})";
                _textSensors.Add( _dataCollector.CreateStringSensor(sensorName));
            }
        }

        private void timer_Tick(object? sender, EventArgs e)
        {


            foreach (var sensor in _textSensors)
            {
                string message = $"{textToSend.Text}_{DateTime.Now.Millisecond}";
                var random = new Random().Next(0, 100);
                string comment = $"Comment: random = {random}";

                HSMSensorDataObjects.SensorStatus status = HSMSensorDataObjects.SensorStatus.Ok;

                if (random > 90)
                    status = HSMSensorDataObjects.SensorStatus.Error;
                else if (random < 30)
                    status = HSMSensorDataObjects.SensorStatus.OffTime;

                sensor.AddValue(message, status, comment);
                

                
            }

            buttonAutoSentText.Text = $"{DateTime.Now.ToShortTimeString}: sent {_textSensors.Count} sensors";
        }
    }


}