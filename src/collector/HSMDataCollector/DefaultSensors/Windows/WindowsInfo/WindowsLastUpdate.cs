using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsLastUpdate : MonitoringSensorBase<TimeSpan>
    {
        private const string ShellCommand = "Get-WinEvent -LogName Setup | where {$_.message -match \"success\"} | select -First 1 -Property @{Name='Date';Expression={$_.TimeCreated.ToString()}} | select -ExpandProperty Date";
        
        
        private readonly DateTime _lastUpdateDate;

        protected override TimeSpan TimerDueTime => _receiveDataPeriod.GetTimerDueTime();


        public WindowsLastUpdate(WindowsInfoSensorOptions options) : base(options)
        {
            using (var process = ProcessInfo.GetPowershellProcess(ShellCommand))
            {
                process.Start();

                DateTime.TryParse(process.StandardOutput.ReadToEnd(), out _lastUpdateDate);

                process.WaitForExit();
            }
        }


        protected override TimeSpan GetValue() => DateTime.UtcNow - _lastUpdateDate.ToUniversalTime();
    }
}