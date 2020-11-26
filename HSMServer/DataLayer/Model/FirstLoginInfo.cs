using System;
using System.Net;

namespace HSMServer.DataLayer.Model
{
    /// <summary>
    /// Info about the first client connection from a particular address
    /// </summary>
    public class FirstLoginInfo
    {
        public IPAddress Address { get; set; }
        public int Port { get; set; }
        public DateTime Time { get; set; }
    }
}
