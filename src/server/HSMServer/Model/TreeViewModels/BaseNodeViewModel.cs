using HSMServer.Model.Folders;
using System;

namespace HSMServer.Model.TreeViewModel
{
    public abstract class BaseNodeViewModel
    {
        public TimeIntervalViewModel ExpectedUpdateInterval { get; protected set; }

        public TimeIntervalViewModel SensorRestorePolicy { get; protected set; }


        public Guid Id { get; protected set; }

        public string Name { get; protected set; }

        public string Description { get; protected set; }

        public BaseNodeViewModel Parent { get; internal set; }


        public ProductNodeViewModel RootProduct => Parent is null or FolderModel ? (ProductNodeViewModel)this : Parent.RootProduct;
    }
}
