using System.ComponentModel;

namespace HSMSensorDataObjects
{
    [DefaultValue(Ok)]
    public enum SensorStatus
    {
        /// <summary>
        /// The status indicates that the sensor is off by schedule
        /// </summary>
        OffTime = 0,

        /// <summary>
        /// The status indicates that the value is correct, and will be displayed and presented as valid.
        /// </summary>
        Ok = 1,

        /// <summary>
        /// The status indicates that the sensor requires attention, users will be notified if the notifications are configured
        /// </summary>
        Warning = 2,

        /// <summary>
        /// The status indicates sensor error, that will be displayed red in client app by default
        /// </summary>
        Error = 3
    }
}