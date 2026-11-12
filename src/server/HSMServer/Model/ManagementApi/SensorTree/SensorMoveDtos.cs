using System;
using System.Collections.Generic;
using System.ComponentModel;
using HSMServer.Model.ManagementApi.ChangeSets;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// Move sensors to another path (#1721): one sensor to a full new path, or
    /// several sensors into a destination node keeping their names.
    /// </summary>
    // Carries [Description]s on every member: the record rides the MCP tool's
    // INPUT schema too (#1545).
    [Description("Move sensors to another path, in their product or into another root product, keeping each sensor's id, history, settings and alerts. The old path becomes an alias that keeps routing values to the sensor.")]
    public sealed record SensorMoveRequestDto
    {
        /// <summary>Ids of the sensors to move (1..100).</summary>
        [Description("Ids of the sensors to move, 1..100. With `path`, exactly one.")]
        public List<Guid> Sensors { get; init; }

        /// <summary>The destination root product.</summary>
        [Description("Id of the destination ROOT product (a sub-node id is rejected: paths are relative to the root product).")]
        public Guid Product { get; init; }

        /// <summary>Full new path of ONE sensor, relative to the product (may rename it).</summary>
        [Description("Full new path of ONE sensor relative to the product, e.g. `garage/system/CPU` — the last segment is the sensor's new name. Exactly one of `path` and `node`.")]
        public string Path { get; init; }

        /// <summary>Destination node path, relative to the product; every sensor keeps its name.</summary>
        [Description("Destination node path relative to the product, e.g. `garage/system`; each sensor keeps its name. Missing nodes are created. Exactly one of `path` and `node`.")]
        public string Node { get; init; }

        /// <summary>"fail" (default) or "merge".</summary>
        [Description("What happens when a sensor already holds a destination path: `fail` (default) — that sensor is not moved (outcome `conflict`); `merge` — the moved sensor keeps its id, settings and alerts and takes over the other sensor's history (its own value wins a same-timestamp collision), the other sensor is removed. IRREVERSIBLE. Sensors of different types are never merged.")]
        public string OnConflict { get; init; }
    }


    /// <summary>Per-sensor outcome of a move request; the batch is not atomic.</summary>
    [Description("Outcome of a move request: one entry per requested sensor, in request order. Not atomic — each sensor succeeds or fails on its own.")]
    public sealed record SensorMoveResponseDto
    {
        [Description("One entry per requested sensor, in request order.")]
        public List<SensorMoveItemDto> Results { get; init; }

        [Description("Sensors moved (outcome `moved` or `merged`).")]
        public int Moved { get; init; }

        [Description("Sensors not moved (any other outcome but `unchanged`).")]
        public int Failed { get; init; }
    }


    /// <summary>The outcome for one sensor.</summary>
    [Description("The outcome for one sensor.")]
    public sealed record SensorMoveItemDto
    {
        [Description("The sensor id.")]
        public Guid SensorId { get; init; }

        [Description("Value table: `moved`, `merged` (moved and absorbed the sensor that held the path), `unchanged` (already there), `conflict` (a sensor holds the path and no merge was asked, or the types differ), `notFound` (unknown or invisible sensor), `forbidden` (the token's owner may not write this sensor), `invalidPath`, `failed`.")]
        public string Outcome { get; init; }

        [Description("Full path before the move; null when the sensor was not found.")]
        public string OldPath { get; init; }

        [Description("Full path after the move (or the one that was attempted).")]
        public string NewPath { get; init; }

        [Description("Id of the sensor that was absorbed (`merged`) or that holds the path (`conflict`); null otherwise.")]
        public Guid? OtherSensorId { get; init; }

        [Description("Values taken over from the absorbed sensor; set whenever a merge happened (`merged`, or `failed` when the move after the merge failed — the absorbed sensor is gone either way).")]
        public long? MergedValues { get; init; }

        [Description("Absorbed values dropped because this sensor had a value at the same timestamp; set whenever a merge happened.")]
        public long? MergeCollisions { get; init; }

        [Description("Why the sensor was not moved, or a note on a partial merge; null on a clean success.")]
        public string Error { get; init; }
    }


    /// <summary>The dry-run preview of a move request (#1721): nothing was applied.</summary>
    [Description("The dry-run preview of a move request: what each sensor's move would do — nothing was applied.")]
    public sealed record SensorMovePreviewDto : WritePreviewDto
    {
        [Description("One prediction per requested sensor, in request order.")]
        public List<SensorMovePreviewItemDto> Sensors { get; init; }
    }


    /// <summary>The prediction for one sensor.</summary>
    [Description("The predicted move of one sensor.")]
    public sealed record SensorMovePreviewItemDto
    {
        [Description("The sensor id.")]
        public Guid SensorId { get; init; }

        [Description("Predicted outcome; same value table as a move result's `outcome`.")]
        public string Outcome { get; init; }

        [Description("Full path now; null when the sensor was not found.")]
        public string OldPath { get; init; }

        [Description("Full path after the move.")]
        public string NewPath { get; init; }

        [Description("Id of the sensor that would be absorbed (`merged`) or that holds the path (`conflict`).")]
        public Guid? OtherSensorId { get; init; }

        [Description("Id of another moved sensor whose alias (old path) the destination is: the move takes the path over, and values from collectors still sending there would land in this sensor instead.")]
        public Guid? DisplacesAliasOf { get; init; }

        [Description("Destination nodes the move would create, as full paths.")]
        public List<string> CreatedNodes { get; init; }

        [Description("Alert templates whose alerts the move would remove from the sensor (they no longer match at the new path).")]
        public List<SensorMoveTemplateDto> TemplatesDetached { get; init; }

        [Description("Alert templates whose alerts the move would add to the sensor (they match at the new path).")]
        public List<SensorMoveTemplateDto> TemplatesApplied { get; init; }

        [Description("Settings the sensor inherits (\"From parent\") whose effective value would change at the new place.")]
        public List<SensorMoveSettingDto> SettingsChanged { get; init; }

        [Description("Why the move would not happen; null when it would.")]
        public string Error { get; init; }
    }


    [Description("An alert template a move would detach or apply.")]
    public sealed record SensorMoveTemplateDto
    {
        [Description("Alert template id.")]
        public Guid Id { get; init; }

        [Description("Alert template name.")]
        public string Name { get; init; }
    }


    [Description("An inherited setting whose effective value would change.")]
    public sealed record SensorMoveSettingDto
    {
        [Description("Setting name: `TTL`, `KeepHistory` or `SelfDestroy`.")]
        public string Name { get; init; }

        [Description("Effective value now.")]
        public string Before { get; init; }

        [Description("Effective value at the new place.")]
        public string After { get; init; }
    }
}
