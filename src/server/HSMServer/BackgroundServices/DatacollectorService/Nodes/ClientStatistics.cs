using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMServer.ServerConfiguration.Monitoring;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HSMServer.BackgroundServices
{
    public class ClientStatistics
    {        
        private const int DigitsCnt = 2;

        private readonly IDataCollector _collector;
        public readonly IOptionsMonitor<MonitoringOptions> _optionsMonitor;
        private readonly TimeSpan _barInterval = new(0, 1, 0);

        
        private const string RequestsCountPath = "Clients/Requests per second";
        private const string DataCountPath = "Clients/Sensor updates per second";

        private const string ResponseSizePath = "Clients/Response size";
        private const string RequestSizePath = "Clients/Request size per second";
        
        
        internal IParamsFuncSensor<double, double> RequestSizeSensor { get; }

        internal IParamsFuncSensor<double, double> ResponseSizeSensor { get; }

        internal IParamsFuncSensor<double, double> ReceivedDataCountSensor { get; }

        internal IParamsFuncSensor<double, double> RequestsCountSensor { get; }


        public ClientStatistics(IDataCollector collector, IOptionsMonitor<MonitoringOptions> optionsMonitor)
        {
            _collector = collector;
            _optionsMonitor = optionsMonitor;

            RequestSizeSensor = RegisterParamSensor<double>(RequestSizePath);
            ResponseSizeSensor = RegisterParamSensor<double>(ResponseSizePath);
            
            ReceivedDataCountSensor = RegisterParamSensor<double>(DataCountPath);
            RequestsCountSensor = RegisterParamSensor<double>(RequestsCountPath);
        }
        
        private IParamsFuncSensor<T, T> RegisterParamSensor<T>(string path) where T : IFloatingPoint<T>
        {
            static T GetSum(List<T> values)
            {
                return values.Aggregate(T.Zero, (sum, curVal) => sum + curVal);
            }

            var denominator = T.CreateChecked(_barInterval.TotalSeconds);

            return _collector.CreateParamsFuncSensor<T, T>(path, string.Empty, values => T.Round(GetSum(values) / denominator, DigitsCnt, MidpointRounding.AwayFromZero), _barInterval);
        }
    }
}
