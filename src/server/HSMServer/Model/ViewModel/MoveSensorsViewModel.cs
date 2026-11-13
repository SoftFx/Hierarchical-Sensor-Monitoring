using System;
using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    /// <summary>
    /// The tree's "Move" modal (#1722): the selected sensors, the root products
    /// the user may move them into, and the prefilled destination.
    /// </summary>
    public sealed class MoveSensorsViewModel
    {
        /// <summary>The selected sensors' encoded ids, as the tree knows them.</summary>
        public List<string> SensorIds { get; init; } = [];

        /// <summary>Display names of the selected sensors (for the header).</summary>
        public List<string> SensorNames { get; init; } = [];

        /// <summary>Root products the user manages: (id, name), name-ordered.</summary>
        public List<(Guid Id, string Name)> Products { get; init; } = [];

        /// <summary>The first sensor's root product: the preselected destination.</summary>
        public Guid ProductId { get; init; }

        /// <summary>One sensor: the destination is a full path (rename allowed); several: a node.</summary>
        public bool Single => SensorIds.Count == 1;

        /// <summary>Prefill: the sensor's own path (one sensor) or its parent node's path (several).</summary>
        public string Destination { get; init; }
    }


    /// <summary>The modal's POST body (JSON).</summary>
    public sealed class MoveSensorsRequestViewModel
    {
        public List<string> SensorIds { get; set; } = [];

        public Guid ProductId { get; set; }

        /// <summary>Full new path of one sensor, or the destination node of several.</summary>
        public string Destination { get; set; }

        public bool Merge { get; set; }

        public bool DryRun { get; set; }
    }
}
