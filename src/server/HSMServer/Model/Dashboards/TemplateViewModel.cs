using HSMServer.Dashboards;
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


        public List<Guid> Folders { get; set; }

        public Guid Id { get; set; }

        public string Path { get; set; }

        public bool IsApplied { get; set; }


        public PlottedProperty Property { get; set; }

        public PlottedShape Shape { get; set; }

        public string Label { get; set; }


        public bool IsSubscribed { get; set; }


        public TemplateViewModel() { }

        public TemplateViewModel(PanelSubscription subscription, Dictionary<Guid, string> availableFolders)
        {
            foreach (var (id, name) in availableFolders)
                AvailableFolders.Add((id, name));

            Id = subscription.Id;

            Path = subscription.PathTempalte;
            Property = subscription.Property;
            Label = subscription.Label;
            Shape = subscription.Shape;
            Folders = subscription.Folders;
            IsApplied = subscription.IsApplied;
        }


        internal PanelSubscriptionUpdate ToUpdate() =>
            new()
            {
                PathTemplate = Path,
                Property = Property.ToString(),
                Shape = Shape.ToString(),
                Folders = Folders,
                Label = Label,

                IsSubscribed = IsSubscribed,
            };
    }
}