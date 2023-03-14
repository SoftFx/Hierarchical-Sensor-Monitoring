﻿using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Helpers;
using System;

namespace HSMServer.Model.TreeViewModel
{
    public enum SensorStatus
    {
        OffTime,
        Ok,
        Warning,
        Error,
    }


    public abstract class NodeViewModel
    {
        public Guid Id { get; }

        public string EncodedId { get; }
        
        public TimeIntervalViewModel ExpectedUpdateInterval { get; } = new();
        
        
        public required ProductModel RootProduct { get; init; }
        

        public string Name { get; protected set; }
        
        public string Path { get; protected set; }
        
        public string Description { get; protected set; }
        
        public bool IsOwnExpectedUpdateInterval { get; protected set; }

        public DateTime UpdateTime { get; protected set; }

        public SensorStatus Status { get; protected set; }

        
        public virtual bool HasData { get; protected set; }
        

        public NodeViewModel Parent { get; internal set; }
        
   
        public string Tooltip =>
            $"{Name}{Environment.NewLine}{(UpdateTime != DateTime.MinValue ? UpdateTime.ToDefaultFormat() : "no data")}";

        public string Title => Name?.Replace('\\', ' ') ?? string.Empty;


        internal NodeViewModel(Guid id)
        {
            Id = id;
            EncodedId = SensorPathHelper.EncodeGuid(id);
        }

        protected void Update(NodeBaseModel model)
        {
            Name = model.DisplayName;
            Description = model.Description;

            ExpectedUpdateInterval.Update(model.UsedExpectedUpdateInterval?.ToTimeInterval());
            IsOwnExpectedUpdateInterval = model.ExpectedUpdateInterval != null || model.ParentProduct == null;
        }
    }
}
