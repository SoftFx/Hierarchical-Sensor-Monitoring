using System.ComponentModel;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects
{
    [DataContract]
    [DefaultValue(Ok)]
    public enum SensorStatus
    {
        /// <summary>
        /// Default status, which is given to every sensor value unless another is specified. Needs extra settings to be validated in client app
        /// </summary>
        [EnumMember]
        Unknown = 0,

        /// <summary>
        /// The status indicates that the value is correct, and will be displayed and presented as valid.
        /// </summary>
        [EnumMember]
        Ok = 1,

        /// <summary>
        /// The status indicates that the sensor requires attention, users will be notified if the notifications are configured
        /// </summary>
        [EnumMember]
        Warning = 2,

        /// <summary>
        /// The status indicates sensor error, that will be displayed red in client app by default
        /// </summary>
        [EnumMember]
        Error = 3
    }
}