using System;

namespace HSMSensorDataObjects
{
    public abstract class BaseRequest
    {
        [Obsolete("Send key in request header instead")]
        public string Key { get; set; }

        public string Path { get; set; }
    }
}
