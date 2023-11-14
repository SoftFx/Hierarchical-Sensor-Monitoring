using HSMCommon.Collections;
using System;

namespace HSMServer.Model.TreeViewModels
{
    public sealed class SensorRenderedHash : CHash<Guid>
    {
        public bool IsRendered(Guid id) => Count == 0 || Contains(id);
    }
}