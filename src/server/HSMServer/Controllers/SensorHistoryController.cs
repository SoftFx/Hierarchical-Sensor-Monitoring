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
                string units = model.DisplayUnit.HasValue
                    ? model.DisplayUnit.GetDisplayName()
                    : model.SelectedUnit?.GetDisplayName();

                return new SensorInfoViewModel(model.Type,
                    model.Name is "Service alive" or "Service status" ? SensorType.Enum : model.Type,
                    units);
            }

            return _emptyJsonResult;
        }

        [HttpPost]
        public async Task<JsonResult> ChartHistory([FromBody] GetSensorHistoryRequest model)
        {
            if (model == null || !TryGetSensor(model.EncodedId, out var sensor))
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
            
            return processor.GetResultFromValues(sensor, values, model.BarsCount);
        }

        [HttpPost]
        public void ReloadHistoryRequest([FromBody] GetSensorHistoryRequest model)
        {
            StoredUser.History.Reload(model);
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