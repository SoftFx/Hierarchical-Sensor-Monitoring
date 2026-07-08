using HSMCommon.Extensions;
using HSMSensorDataObjects.HistoryRequests;
using HSMServer.ApiObjectsConverters;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Helpers;
using HSMServer.Model.Dashboards;
using HSMServer.Model.History;
using HSMServer.Model.Model.History;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using HSMServer.JsonConverters;
using System.Linq;
using HSMCommon.Model;

namespace HSMServer.Controllers
{
    public enum HistoryPeriod
    {
        [Display(Name = "Default (300 items)")]
        Default,
        [Display(Name = "Last day")]
        Day,
        [Display(Name = "Last 3 days")]
        ThreeDays,
        [Display(Name = "Last week")]
        Week,
        [Display(Name = "Last 2 weeks")]
        TwoWeeks,
        [Display(Name = "Last month")]
        Month,
        Custom,
    }


    [Authorize]
    public class SensorHistoryController : BaseController
    {


        private readonly JsonSerializerOptions _serializationsOptions = new()
        {
            Converters = { new VersionSourceConverter() },
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        
        internal const int MaxHistoryCount = -TreeValuesCache.MaxHistoryCount;
        private const int LatestHistoryCount = -300;
        private const int SensorValuesCount = -5000;

        // Latest N values per child sensor for the node overlay chart (issue #1235). Negative = take the
        // most recent within the window; bounds the payload when many children are overlaid at once.
        private const int NodeChartMaxPointsPerSensor = -2000;

        private readonly ITreeValuesCache _cache;
        private readonly TreeViewModel _tree;


        private HistoryTableViewModel SelectedTable => StoredUser.History.Table;


        public SensorHistoryController(ITreeValuesCache cache, TreeViewModel tree, IUserManager userManager) : base(userManager)
        {
            _cache = cache;
            _tree = tree;
        }


        [HttpPost]
        public Task<IActionResult> TabelHistoryLatest([FromBody] GetSensorHistoryRequest model)
        {
            if (model == null)
                return Task.FromResult(_emptyResult as IActionResult);

            return TableHistory(SpecifyLatestHistoryModel(model));
        }

        [HttpPost]
        public async Task<IActionResult> TableHistory([FromBody] GetSensorHistoryRequest model)
        {
            if (model == null)
                return _emptyResult;

            await StoredUser.History.Reload(_cache, model);
            return GetHistoryTable(SelectedTable);
        }

        [HttpGet]
        public IActionResult GetPreviousTablePage()
        {
            return GetHistoryTable(SelectedTable?.ToPreviousPage());
        }

        [HttpGet]
        public async Task<IActionResult> GetNextTablePage()
        {
            var result = GetHistoryTable(await SelectedTable?.ToNextPage());
            return result;
        }


        [HttpPost]
        public Task<JsonResult> ChartHistoryLatest([FromBody] GetSensorHistoryRequest model)
        {
            if (model == null)
                return Task.FromResult(_emptyJsonResult);

            return ChartHistory(SpecifyLatestHistoryModel(model));
        }

        [HttpGet]
        public ActionResult<SensorInfoViewModel> GetSensorPlotInfo([FromQuery] Guid id)
        {
            if (_tree.Sensors.TryGetValue(id, out SensorNodeViewModel model))
            {
                string units = IsRateSensor(model)
                    ? model.DisplayUnit.GetDisplayName()
                    : model.SelectedUnit?.GetDisplayName();

                return new SensorInfoViewModel(model.Type,
                    model.Name is "Service alive" or "Service status" ? SensorType.Enum : model.Type,
                    units);
            }

            return _emptyJsonResult;
        }

        private bool IsRateSensor(SensorNodeViewModel model)
        {
            if(model == null) return false;

            return model.Type == SensorType.Rate && model.DisplayUnit.HasValue;
        }

        [HttpPost]
        public async Task<JsonResult> ChartHistory([FromBody] GetSensorHistoryRequest model)
        {
            if (model == null || !TryGetSensor(model.EncodedId, out SensorNodeViewModel sensor))
                return _emptyJsonResult;
            
            var values = await GetSensorValues(model.EncodedId, model.FromUtc, model.ToUtc, model.Count, model.Options);

            var localValue = GetLocalLastValue(model.EncodedId, model.FromUtc, model.ToUtc);

            if (localValue is not null && (values.Count == 0 || values[0].Time != localValue.Time))
                values.Add(localValue);

            var processor = HistoryProcessorFactory.BuildProcessor((int) sensor.Type);

            if (processor is VersionHistoryProcessor versionProcessor)
            {
                var cacheSensor = _cache.GetSensor(sensor.Id);
                versionProcessor.AttachSensor(cacheSensor);
            }

            List<BaseValue> displayValues = values.Select(v => sensor.ToDisplayValue(v)).ToList();

            return processor.GetResultFromValues(sensor, displayValues, model.BarsCount);
        }

        [HttpPost]
        public void ReloadHistoryRequest([FromBody] GetSensorHistoryRequest model)
        {
            StoredUser.History.Reload(model);
        }

        /// <summary>
        /// Node-level overlay chart (issue #1235): returns every comparable child sensor of the node as
        /// one line series over the chosen window. v1 overlays the node's largest (type, unit) group only.
        /// Read-only; children with no data in the window are omitted (drawn with gaps, never zero-filled).
        /// </summary>
        [HttpPost]
        public async Task<JsonResult> NodeChartHistory([FromBody] NodeChartRequest request)
        {
            if (request is null)
                return _emptyJsonResult;

            var nodeId = request.NodeId.ToGuid();

            if (!_tree.Nodes.TryGetValue(nodeId, out var node))
                return _emptyJsonResult;

            var groups = _tree.GetComparableChildGroups(nodeId);

            if (groups.Count == 0)
                return new JsonResult(new { error = false, series = Array.Empty<object>() }, _serializationsOptions);

            var group = groups[0];
            var from = request.From.ToUtcKind();
            var to = request.To.ToUtcKind();
            var nodePath = node.FullPath;

            var series = new List<object>(group.SensorIds.Count);

            foreach (var sensorId in group.SensorIds)
            {
                if (!_tree.Sensors.TryGetValue(sensorId, out var sensor))
                    continue;

                var values = await BuildNodeChartSeries(sensor, from, to);

                if (values.Count == 0) // omitted, not zero-filled
                    continue;

                series.Add(new
                {
                    id = sensor.Id,
                    label = GetNodeRelativeLabel(nodePath, sensor.FullPath),
                    values,
                });
            }

            var note = groups.Count > 1
                ? $"Showing {series.Count} sensor(s) in {(string.IsNullOrEmpty(group.UnitLabel) ? "no unit" : group.UnitLabel)}. This node also has comparable sensors in other units, which are not overlaid here."
                : null;

            return new JsonResult(new
            {
                error = false,
                unit = group.UnitLabel,
                note,
                series,
            }, _serializationsOptions);
        }

        private async Task<List<object>> BuildNodeChartSeries(SensorNodeViewModel sensor, DateTime from, DateTime to)
        {
            var rawValues = await GetSensorValues(sensor.EncodedId, from, to, NodeChartMaxPointsPerSensor);

            var points = new List<(DateTime Time, double Value)>(rawValues.Count);

            foreach (var raw in rawValues)
            {
                var display = sensor.ToDisplayValue(raw);

                if (TryGetScalar(display, out var scalar))
                    points.Add((display.Time.ToUniversalTime(), scalar));
            }

            return points.OrderBy(point => point.Time)
                         .Select(point => (object)new { time = point.Time, value = point.Value })
                         .ToList();
        }

        private static bool TryGetScalar(BaseValue value, out double scalar)
        {
            switch (value)
            {
                case BaseValue<double> doubleValue:
                    scalar = doubleValue.Value;
                    return true;
                case BaseValue<int> intValue:
                    scalar = intValue.Value;
                    return true;
                case BarBaseValue<double> doubleBar:
                    scalar = doubleBar.Mean;
                    return true;
                case BarBaseValue<int> intBar:
                    scalar = intBar.Mean;
                    return true;
                default:
                    scalar = 0;
                    return false;
            }
        }

        private static string GetNodeRelativeLabel(string nodePath, string sensorPath)
        {
            if (!string.IsNullOrEmpty(nodePath) && sensorPath.StartsWith(nodePath, StringComparison.Ordinal))
            {
                var relative = sensorPath.Substring(nodePath.Length).TrimStart('/');

                if (relative.Length > 0)
                    return relative;
            }

            return sensorPath;
        }

        [HttpGet]
        public IActionResult GetBackgroundSensorInfo([FromQuery] Guid currentId, [FromQuery] bool isStatusService = false)
        {
            if (TryGetBackgroundSensorInfo(currentId, isStatusService, out var id, out string path))
                return Json(new
                {
                    id,
                    path
                });

            return _emptyJsonResult;
        }

        private bool TryGetBackgroundSensorInfo(Guid currentId, bool isStatusService, out Guid id, out string path)
        {
            id = _tree.Sensors.TryGetValue(currentId, out var sensor) ? _tree.GetBackgroundPlotId(sensor, isStatusService) : Guid.Empty;

            _tree.Sensors.TryGetValue(id, out var sensorNodeViewModel);
            path = sensorNodeViewModel?.FullPath;

            return id != Guid.Empty;
        }

        [HttpPost]
        public Task<JsonResult> GetServiceStatusHistory([FromBody] GetSensorHistoryRequest model, [FromQuery] bool isStatusService = false)
        {
            var currentId = SensorPathHelper.DecodeGuid(model.EncodedId);

            var oldPath = $".module/Module Info/{(isStatusService ? "Service status" : "Service alive")}";
            var newPath = $".module/{(isStatusService ? "Service status" : "Service alive")}";

            if (_tree.Sensors.TryGetValue(currentId, out var sensor) && (sensor.Path.EndsWith(oldPath) || sensor.Path.EndsWith(newPath)))
                return ChartHistory(model with { EncodedId = sensor.Id.ToString() });

            return TryGetBackgroundSensorInfo(currentId, isStatusService, out var id, out _)
                ? ChartHistory(model with { EncodedId = id.ToString() })
                : Task.FromResult(_emptyJsonResult);
        }

        

        public async Task<FileResult> ExportHistory([FromQuery(Name = "EncodedId")] string encodedId, [FromQuery(Name = "Type")] int type,
            [FromQuery] bool addHiddenColumns, [FromQuery(Name = "From")] DateTime from, [FromQuery(Name = "To")] DateTime to)
        {
            if (!TryGetSensor(encodedId, out var sensor))
                return null;

            //doesn't match with visivle table values
            //var values = await GetSensorValues(encodedId, from.ToUtcKind(), to.ToUtcKind(), MaxHistoryCount, RequestOptions.IncludeTtl);

            if (!IsTableSelected())
            {
                GetSensorHistoryRequest request = new GetSensorHistoryRequest { EncodedId = encodedId };

                if (from == DateTime.MinValue)
                {
                    //Default 300
                    request.From = DateTime.MinValue;
                    request.To = DateTime.Now.AddDays(2);
                    request.Count = LatestHistoryCount;
                }
                else
                {
                    // time interval
                    request.From = from;
                    request.To = to;
                    request.Count = SensorValuesCount;
                }

                //set SelectedTable
                await TableHistory(request);
            }

            var values = await SelectedTable.GetAllValues();


            //case Default 300
            if (from == DateTime.MinValue && values.Any())
            {
                from = values.Last().LastUpdateTime;
                to = values[0].LastUpdateTime;
            }

            string fileName = $"{sensor.FullPath.Replace('/', '_')}_from_{from:s}_to{to:s}.csv";
            Response.Headers["Content-Disposition"] = $"attachment;filename={fileName}";

            var exportOptions = BuildExportOptions(encodedId, addHiddenColumns);
            var content = Encoding.UTF8.GetBytes(values.ConvertToCsv(exportOptions));

            return File(content, fileName.GetContentType(), fileName);
        }

        private bool IsTableSelected()
        {
            if(SelectedTable == null)
                return false;

            return SelectedTable.LastIndex >= 0;
        }


        private PartialViewResult GetHistoryTable(HistoryTableViewModel viewModel) => PartialView("_SensorValuesTable", viewModel);


        private ValueTask<List<BaseValue>> GetSensorValues(string encodedId, DateTime from, DateTime to, int count, RequestOptions options = default)
        {
            if (string.IsNullOrEmpty(encodedId))
                return new(new List<BaseValue>());

            return _cache.GetSensorValuesPage(SensorPathHelper.DecodeGuid(encodedId), from, to, count, options).Flatten();
        }

        private GetSensorHistoryRequest SpecifyLatestHistoryModel(GetSensorHistoryRequest model)
        {
            if (!TryGetSensor(model.EncodedId, out var sensor))
                return null;

            var lastUpdate = sensor?.LastValue?.ReceivingTime ?? DateTime.MinValue;
            var lastTimeout = sensor?.LastTimeout?.ReceivingTime ?? DateTime.MinValue;

            model.From = DateTime.MinValue;
            model.To = lastTimeout > lastUpdate ? lastTimeout : lastUpdate;
            model.Count = LatestHistoryCount;

            return model;
        }


        private BarBaseValue GetLocalLastValue(string encodedId, DateTime from, DateTime to)
        {
            var sensor = _cache.GetSensor(SensorPathHelper.DecodeGuid(encodedId));

            var localValue = sensor is IBarSensor barSensor ? barSensor.LocalLastValue : null;

            return localValue?.ReceivingTime >= from && localValue?.ReceivingTime <= to ? localValue : null;
        }

        private bool TryGetSensor(string encodedId, out SensorNodeViewModel sensor) =>
            _tree.Sensors.TryGetValue(encodedId.ToGuid(), out sensor);

        private ExportOptions BuildExportOptions(string sensorId, bool addHiddenColumns)
        {
            ExportOptions exportOptions = ExportOptions.Simple;

            if (TryGetSensor(sensorId, out var sensor))
            {
                if (addHiddenColumns)
                    exportOptions |= ExportOptions.Hidden;
                if (sensor.AggregateValues)
                    exportOptions |= ExportOptions.Aggregated;
                if (sensor.IsEma)
                    exportOptions |= ExportOptions.EmaStatistics;
            }

            return exportOptions;
        }
    }
}