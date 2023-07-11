using HSMServer.Core.Model;
using System;
using System.Text;

namespace HSMServer.Core.Cache.UpdateEntities
{
    public abstract record BaseNodeUpdate : IUpdateComparer<BaseNodeModel, BaseNodeUpdate>
    {
        public required Guid Id { get; init; }


        public TimeIntervalModel TTL { get; init; }

        public TimeIntervalModel KeepHistory { get; init; }

        public TimeIntervalModel RestoreInterval { get; init; }

        public TimeIntervalModel SelfDestroy { get; init; }

        public string Description { get; init; }

        public string Compare(BaseNodeModel entity, BaseNodeUpdate update)
        {
            var builder = new StringBuilder();

            if (entity.Description != update.Description && update.Description is not null)
                builder.AppendLine($"Description: {entity.Description} -> {update.Description}");
            
            return builder.ToString();
        }
    }
}
