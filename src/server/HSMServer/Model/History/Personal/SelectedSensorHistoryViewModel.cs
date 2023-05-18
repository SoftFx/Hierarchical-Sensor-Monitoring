using System;

namespace HSMServer.Model.History
{
    public sealed class SelectedSensorHistoryViewModel
    {
        private Guid _selectedSensorId;


        public ChartValuesViewModel Chart { get; private set; }

        public TableValuesViewModel Table { get; set; } //todo close


        public void LoadSensor(Guid newId)
        {
            if (newId == _selectedSensorId)
                return;
        }
    }
}
