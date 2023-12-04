using HSMServer.Core;
using HSMServer.Core.Model;
using HSMServer.Model.TreeViewModel;
using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    public sealed class SensorInfoViewModel : NodeInfoBaseViewModel
    {
        public List<Unit> AvailableUnits { get; }


        public SensorType SensorType { get; }

        public string StatusComment { get; }

        public bool IsSingleton { get; }

        public bool HasGrafana { get; }

        public bool IsMuted { get; }


        public bool IsEMA { get; set; }

        public Unit? SelectedUnit { get; set; }

        public bool AggregateValues { get; set; }


        // public constructor without parameters for action Home/UpdateSensorInfo
        public SensorInfoViewModel() : base() { }

        internal SensorInfoViewModel(SensorNodeViewModel sensor) : base(sensor)
        {
            SensorType = sensor.Type;
            StatusComment = sensor.ValidationError;
            IsMuted = sensor.State == SensorState.Muted;
            HasGrafana = sensor.Integration.HasGrafana();
            IsEMA = sensor.Statistics.HasFlag(StatisticsOptions.EMA);

            IsSingleton = sensor.IsSingleton;
            SelectedUnit = sensor.SelectedUnit;
            AvailableUnits = sensor.AvailableUnits;
            AggregateValues = sensor.AggregateValues;
        }


        internal StatisticsOptions GetOptions()
        {
            var options = StatisticsOptions.None;

            if (IsEMA)
                options |= StatisticsOptions.EMA;

            return options;
        }
    }
}
