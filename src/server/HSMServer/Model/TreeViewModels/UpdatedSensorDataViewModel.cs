﻿using HSMServer.Extensions;

namespace HSMServer.Model.TreeViewModels
{
    public record UpdatedNodeDataViewModel
    {
        public string Id { get; }

        public string Status { get; }

        public string StatusIconColorClass { get; }

        public string GridCellColorClass { get; }

        public string UpdatedTimeStr { get; }

        public string Tooltip { get; }


        internal UpdatedNodeDataViewModel(NodeViewModel node)
        {
            Id = node.EncodedId;
            Status = node.Status.ToString();
            StatusIconColorClass = node.Status.ToCssIconClass();
            GridCellColorClass = node.Status.ToCssGridCellClass();
            UpdatedTimeStr = $"updated {node.GetTimeAgo()}";
            Tooltip = node.Tooltip;
        }
    }


    public record UpdatedSensorDataViewModel : UpdatedNodeDataViewModel
    {
        public string Value { get; }

        public string ValidationError { get; }

        public bool IsValidationErrorVisible { get; }


        public UpdatedSensorDataViewModel(SensorNodeViewModel sensor) : base(sensor)
        {
            Value = sensor.ShortStringValue;
            ValidationError = sensor.ValidationError;
            IsValidationErrorVisible = sensor.IsValidationErrorVisible;
        }
    }
}
