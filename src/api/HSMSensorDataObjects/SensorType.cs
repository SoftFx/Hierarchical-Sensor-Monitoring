﻿namespace HSMSensorDataObjects
{
    public enum SensorType
    {

        /// <summary>
        /// Simple sensor which collects data of boolean type and sends the collected data instantly
        /// </summary>
        BooleanSensor = 0,

        /// <summary>
        /// Simple sensor which collects data of integer type and sends the collected data instantly
        /// </summary>
        IntSensor = 1,

        /// <summary>
        /// Simple sensor which collects data of double type and sends the collected data instantly
        /// </summary>
        DoubleSensor = 2,

        /// <summary>
        /// Simple sensor which collects data of string type and sends the collected data instantly
        /// </summary>
        StringSensor = 3,

        /// <summary>
        /// The sensor collects integer values during the 5-minute period and creates the object with
        /// min, max and mean values, the first value time, the last value time, and values count.
        /// Once the 5-minute period is over, the data is sent to the server and sensor starts collecting new bar.
        /// All values must be integers
        /// </summary>
        IntegerBarSensor = 4,

        /// <summary>
        /// The sensor collects integer values during the 5-minute period and creates the object with
        /// min, max and mean values, the first value time, the last value time, and values count.
        /// Once the 5-minute period is over, the data is sent to the server and sensor starts collecting new bar.
        /// All values must have type double
        /// </summary>
        DoubleBarSensor = 5,

        /// <summary>
        /// The sensor value is a file, as the above one. The only difference is that the file contents are represented as byte array,
        /// not as a string. This type is used for pdf files, which really depend on encoding. You should also use this type if
        /// you have another file, for which encoding is important. 
        /// </summary>
        FileSensor = 6,
        
        /// <summary>
        /// Simple sensor which collects data of TimeSpan type and sends the collected data instantly
        /// </summary>
        TimeSpanSensor = 7,
        
        /// <summary>
        /// Simple sensor which collects data of Version type and sends the collected data instantly
        /// </summary>
        VersionSensor = 8,

        /// <summary>
        /// Simple sensor which collects data of double type in speed format and sends the collected data instantly
        /// </summary>
        RateSensor = 9,
    }
}