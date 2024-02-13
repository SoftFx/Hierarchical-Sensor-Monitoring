﻿using HSMServer.Dashboards;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.Dashboards
{
    public sealed class TemplateViewModel
    {
        public const string AnyFolders = "--Any--";
        public const string OtherProducts = "Other products";


        public List<(Guid?, string)> AvailableFolders { get; } = [];

        public List<PlottedProperty> AvailableProperties { get; } =
        [
            PlottedProperty.Value,
            PlottedProperty.EmaValue,
            PlottedProperty.Min,
            PlottedProperty.Mean,
            PlottedProperty.Max,
            PlottedProperty.Count,
            PlottedProperty.EmaMin,
            PlottedProperty.EmaMean,
            PlottedProperty.EmaMax,
            PlottedProperty.EmaCount,
        ];


        public Guid Id { get; set; }

        public string Path { get; set; }

        public List<Guid> Folders { get; set; }


        public PlottedProperty Property { get; set; }

        public PlottedShape Shape { get; set; }

        public string Label { get; set; }


        public TemplateViewModel() { }

        public TemplateViewModel(Dictionary<Guid, string> availableFolders)
        {
            foreach (var (id, name) in availableFolders)
                AvailableFolders.Add((id, name));
        }

        public TemplateViewModel(PanelSubscription subscription, Dictionary<Guid, string> availableFolders) : this(availableFolders)
        {
            Id = subscription.Id;

            Path = subscription.PathTempalte;
            Property = subscription.Property;
            Label = subscription.Label;
            Shape = subscription.Shape;
            Folders = subscription.Folders;
        }


        internal PanelSubscriptionUpdate ToUpdate() =>
            new()
            {
                PathTemplate = Path,
                Property = Property.ToString(),
                Shape = Shape.ToString(),
                Folders = Folders,
                Label = Label,
            };
    }
}
