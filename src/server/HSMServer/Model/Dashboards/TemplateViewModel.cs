using HSMServer.Dashboards;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.Dashboards
{
    public sealed class TemplateViewModel
    {
        private const string AnyFolders = "Any";


        public Dictionary<Guid, string> AvailableFolders { get; } = new Dictionary<Guid, string>()
        {
            { Guid.Empty, AnyFolders }
        };

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

        public Guid Folder { get; set; }

        public string Path { get; set; }


        public PlottedProperty Property { get; set; }

        public PlottedShape Shape { get; set; }

        public string Label { get; set; }


        public TemplateViewModel() { }

        public TemplateViewModel(PanelSubscription subscription)
        {
            Id = subscription.Id;

            Path = subscription.PathTempalte;
            Property = subscription.Property;
            Label = subscription.Label;
            Shape = subscription.Shape;
        }


        internal PanelSubscriptionUpdate ToUpdate() =>
            new()
            {
                PathTemplate = Path,
                Property = Property.ToString(),
                Shape = Shape.ToString(),
                Label = Label,
            };
    }
}
