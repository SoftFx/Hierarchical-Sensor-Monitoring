using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public record DashboardEntity : BaseServerEntity
    {
        public List<DashboardPanelEntity> Panels { get; init; }
    }
}