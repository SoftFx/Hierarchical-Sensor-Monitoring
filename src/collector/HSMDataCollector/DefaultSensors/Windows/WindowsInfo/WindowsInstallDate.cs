using System;
using System.Management;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;


namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsInstallDate : MonitoringSensorBase<TimeSpan>
    {
        public const string WMI_OBJECT = "Win32_OperatingSystem=@";

        private ManagementObject _managementObject = new ManagementObject(WMI_OBJECT);

        protected override TimeSpan TimerDueTime => BarTimeHelper.GetTimerDueTime(TimeSpan.FromMinutes(1));//BarTimeHelper.GetTimerDueTime(PostTimePeriod);


        public WindowsInstallDate(WindowsInfoSensorOptions options) : base(options) { }


        protected override TimeSpan GetValue()
        {
            _managementObject.Get();
            var installDate = ManagementDateTimeConverter.ToDateTime(_managementObject["InstallDate"].ToString()).ToUniversalTime();
            return DateTime.UtcNow - installDate;
        }
    }
}
