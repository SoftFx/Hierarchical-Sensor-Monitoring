﻿using HSMSensorDataObjects.HistoryRequests;
using HSMServer.ApiObjectsConverters;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Helpers;
using HSMServer.Model.History;
using HSMServer.Model.Model.History;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    [Authorize]
    public class SensorHistoryController : BaseController
    {
        internal const int MaxHistoryCount = -TreeValuesCache.MaxHistoryCount;
        private const int LatestHistoryCount = -300;

        private readonly ITreeValuesCache _cache;
        private readonly TreeViewModel _tree;


        private HistoryTableViewModel SelectedTable => StoredUser.History.Table;


        public SensorHistoryController(ITreeValuesCache cache, TreeViewModel tree, IUserManager userManager) : base(userManager)
        {
            _cache = cache;
            _tree = tree;
        }


        [HttpPost]
        public Task<IActionResult> TabelHistoryLatest([FromBody] GetSensorHistoryModel model)
        {
            if (model == null)
                return Task.FromResult(_emptyResult as IActionResult);

            return TableHistory(SpecifyLatestHistoryModel(model));
        }

        [HttpPost]
        public async Task<IActionResult> TableHistory([FromBody] GetSensorHistoryModel model)
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
            return GetHistoryTable(await SelectedTable?.ToNextPage());
        }


        [HttpPost]
        public Task<JsonResult> ChartHistoryLatest([FromBody] GetSensorHistoryModel model)
        {
            if (model == null)
                return Task.FromResult(_emptyJsonResult);

            return ChartHistory(SpecifyLatestHistoryModel(model));
        }

        [HttpGet]
        public IActionResult GetSensorPlotInfo([FromQuery] Guid id)
        {
            if (_tree.Sensors.TryGetValue(id, out var sensorNodeViewModel))
                return Json(new
                {
                    realType = sensorNodeViewModel.Type,
                    plotType = sensorNodeViewModel.Name is "Service alive" or "Service status" ? SensorType.Enum : sensorNodeViewModel.Type
                });


            return _emptyJsonResult;
        }

        [HttpPost]
        public async Task<JsonResult> ChartHistory([FromBody] GetSensorHistoryModel model)
        {
            if (model == null)
                return _emptyJsonResult;

            var sensor = GetSensor(model.EncodedId);
            var values = await GetSensorValues(model.EncodedId, model.FromUtc, model.ToUtc, model.Count, model.Options);

            var localValue = GetLocalLastValue(model.EncodedId, model.FromUtc, model.ToUtc);

            if (localValue is not null && (values.Count == 0 || values[0].Time != localValue.Time))
                values.Add(localValue);

            return new JsonResult(HistoryProcessorFactory.BuildProcessor(model.Type)
                                                         .ProcessingAndCompression(sensor, values, model.BarsCount)
                                                         .Select(v => (object)v));
        }


        [HttpPost]
        public void ReloadHistoryRequest([FromBody] GetSensorHistoryModel model)
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
        public Task<JsonResult> GetServiceStatusHistory([FromBody] GetSensorHistoryModel model, [FromQuery] bool isStatusService = false)
        {
            var currentId = SensorPathHelper.DecodeGuid(model.EncodedId);
            if (_tree.Sensors.TryGetValue(currentId, out var sensor) && sensor.Path.EndsWith($".module/Module Info/{(isStatusService ? "Service status" : "Service alive")}"))
                return ChartHistory(model with { EncodedId = sensor.Id.ToString() });

            return TryGetBackgroundSensorInfo(currentId, isStatusService, out var id, out _)
                ? ChartHistory(model with { EncodedId = id.ToString() })
                : Task.FromResult(_emptyJsonResult);
        }

        public async Task<FileResult> ExportHistory([FromQuery(Name = "EncodedId")] string encodedId, [FromQuery(Name = "Type")] int type,
            [FromQuery(Name = "From")] DateTime from, [FromQuery(Name = "To")] DateTime to)
        {
            var sensor = GetSensor(encodedId);

            string fileName = $"{sensor.FullPath.Replace('/', '_')}_from_{from:s}_to{to:s}.csv";
            Response.Headers.Add("Content-Disposition", $"attachment;filename={fileName}");

            var values = await GetSensorValues(encodedId, from.ToUtcKind(), to.ToUtcKind(), MaxHistoryCount);
            var content = Encoding.UTF8.GetBytes(values.ConvertToCsv());

            return File(content, fileName.GetContentType(), fileName);
        }


        private PartialViewResult GetHistoryTable(HistoryTableViewModel viewModel) => PartialView("_SensorValuesTable", viewModel);


        private ValueTask<List<BaseValue>> GetSensorValues(string encodedId, DateTime from, DateTime to, int count, RequestOptions options = default)
        {
            if (string.IsNullOrEmpty(encodedId))
                return new(new List<BaseValue>());

            return _cache.GetSensorValuesPage(SensorPathHelper.DecodeGuid(encodedId), from, to, count, options).Flatten();
        }

        private GetSensorHistoryModel SpecifyLatestHistoryModel(GetSensorHistoryModel model)
        {
            var sensor = GetSensor(model.EncodedId);
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

        private SensorNodeViewModel GetSensor(string encodedId)
        {
            _tree.Sensors.TryGetValue(encodedId.ToGuid(), out var sensor);

            return sensor;
        }
    }
}