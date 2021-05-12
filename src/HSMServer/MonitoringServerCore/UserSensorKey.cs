using System;
using System.Collections.Generic;

namespace HSMServer.MonitoringServerCore
{
    internal struct UserSensorKey
    {
        private string _userName;
        private string _productName;
        private string _sensorPath;

        public UserSensorKey(string userName, string productName, string sensorPath)
        {
            _userName = userName;
            _productName = productName;
            _sensorPath = sensorPath;
        }

        public string ProductName => _productName;
        public string Path => _sensorPath;
        public class EqualityComparer : IEqualityComparer<UserSensorKey>
        {
            public bool Equals(UserSensorKey x, UserSensorKey y)
            {
                return x._userName == y._userName && x._productName == y._productName && x._sensorPath == y._sensorPath;
            }

            public int GetHashCode(UserSensorKey obj)
            {
                return HashCode.Combine(obj._userName, obj._productName, obj._sensorPath);
            }
        }
    }
}
