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

        public bool HasGrafana { get; }

        public bool IsMuted { get; }


        public bool SaveOnlyUniqueValues { get; set; }

        public Unit? SelectedUnit { get; set; }


        // public constructor without parameters for action Home/UpdateSensorInfo
        public SensorInfoViewModel() : base() { }

        internal SensorInfoViewModel(SensorNodeViewModel sensor) : base(sensor)
        {
            SensorType = sensor.Type;
            StatusComment = sensor.ValidationError;
            HasGrafana = sensor.Integration.HasGrafana();
            IsMuted = sensor.State == SensorState.Muted;

            SelectedUnit = sensor.SelectedUnit;
            AvailableUnits = sensor.AvailableUnits;
            SaveOnlyUniqueValues = sensor.SaveOnlyUniqueValues;
        }
    }
}
