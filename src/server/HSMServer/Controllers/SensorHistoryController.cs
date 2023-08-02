﻿using HSMServer.ApiObjectsConverters;
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

        [HttpPost]
        public async Task<JsonResult> ChartHistory([FromBody] GetSensorHistoryModel model)
        {
            if (model == null)
                return _emptyJsonResult;

            var values = await GetSensorValues(model.EncodedId, model.FromUtc, model.ToUtc, model.Count);

            var localValue = GetLocalLastValue(model.EncodedId, model.FromUtc, model.ToUtc);

            if (localValue is not null && (values.Count == 0 || values[0].Time != localValue.Time))
                values.Add(localValue);

            return new JsonResult(HistoryProcessorFactory.BuildProcessor(model.Type)
                                                         .ProcessingAndCompression(values, model.BarsCount)
                                                         .Select(v => (object)v));
        }


        [HttpPost]
        public void ReloadHistoryRequest([FromBody] GetSensorHistoryModel model)
        {
            StoredUser.History.Reload(model);
        }

        [HttpPost]
        public Task<JsonResult> GetServiceStatusHistory([FromBody] GetSensorHistoryModel model)
        {
            _tree.Sensors.TryGetValue(SensorPathHelper.DecodeGuid(model.EncodedId), out var firstSensor);

            SensorNodeViewModel FindSensor(ProductNodeViewModel node)
            {
                if (node.Name == "Product Info")
                    return node.Sensors.FirstOrDefault(x => x.Value.Name == "Service status").Value;

                return null;
            }

            var dict = new Dictionary<Guid, byte>();
           
            var sensors = TryFindUp(firstSensor.Parent);
          
            
            static Guid CountSameParents(List<SensorNodeViewModel> sensors, SensorNodeViewModel sensor)
            {
                var response = Guid.Empty;
                var max = 0;
                foreach (var currSensor in sensors)
                {
                    var compared = Compare(currSensor, sensor);
                    if (compared > max)
                    {
                        response = currSensor.Id;
                        max = compared;
                    }
                }

                return response;

                int Compare(NodeViewModel currSensor, NodeViewModel sensor)
                {
                    var first = currSensor.FullPath.Split('/');
                    var second = sensor.FullPath.Split('/');
                    int i = 0;

                    while (i < first.Length && i < second.Length && first[i] == second[i])
                        i++;

                    return i;
                }
            }
            
            SensorNodeViewModel TryFindDown(BaseNodeViewModel node)
            {
                if (node is ProductNodeViewModel parent && !dict.TryGetValue(parent.Id, out _))
                {
                    dict.TryAdd(parent.Id, byte.MinValue);
                    var sensor = FindSensor(parent);
                    if (sensor is not null)
                        return sensor;
                    
                    foreach (var (_, subnode) in parent.Nodes)
                    {
                        sensor = TryFindDown(subnode);
                        if (sensor is not null)
                            return sensor;
                    }
                }

                return null;
            }
            
            List<SensorNodeViewModel> TryFindUp(BaseNodeViewModel node)
            {
                if (node is ProductNodeViewModel parent)
                {
                    var sensors = parent?.Nodes?.Select(x => TryFindDown(x.Value)).Where(x => x is not null).ToList();

                    if (sensors.Count > 0)
                        return sensors;
                    
                    if (parent.Parent is ProductNodeViewModel)
                        return TryFindUp(parent.Parent);
                }

                return null;
            }

            var id = CountSameParents(sensors, firstSensor);
            if (id == Guid.Empty)
                return Task.FromResult(_emptyJsonResult);

            return ChartHistory(SpecifyLatestHistoryModel(model with { EncodedId = id.ToString() }));
        }
        
        public async Task<FileResult> ExportHistory([FromQuery(Name = "EncodedId")] string encodedId, [FromQuery(Name = "Type")] int type,
            [FromQuery(Name = "From")] DateTime from, [FromQuery(Name = "To")] DateTime to)
        {
            _tree.Sensors.TryGetValue(SensorPathHelper.DecodeGuid(encodedId), out var sensor);

            string fileName = $"{sensor.FullPath.Replace('/', '_')}_from_{from:s}_to{to:s}.csv";
            Response.Headers.Add("Content-Disposition", $"attachment;filename={fileName}");

            var values = await GetSensorValues(encodedId, from.ToUtcKind(), to.ToUtcKind(), MaxHistoryCount);
            var content = Encoding.UTF8.GetBytes(values.ConvertToCsv());

            return File(content, fileName.GetContentType(), fileName);
        }


        private PartialViewResult GetHistoryTable(HistoryTableViewModel viewModel) => PartialView("_SensorValuesTable", viewModel);


        private ValueTask<List<BaseValue>> GetSensorValues(string encodedId, DateTime from, DateTime to, int count)
        {
            if (string.IsNullOrEmpty(encodedId))
                return new(new List<BaseValue>());

            return _cache.GetSensorValuesPage(SensorPathHelper.DecodeGuid(encodedId), from, to, count).Flatten();
        }

        private GetSensorHistoryModel SpecifyLatestHistoryModel(GetSensorHistoryModel model)
        {
            _tree.Sensors.TryGetValue(SensorPathHelper.DecodeGuid(model.EncodedId), out var sensor);

            model.From = DateTime.MinValue;
            model.To = sensor?.LastValue?.ReceivingTime ?? DateTime.MinValue;
            model.Count = LatestHistoryCount;

            return model;
        }


        private BarBaseValue GetLocalLastValue(string encodedId, DateTime from, DateTime to)
        {
            var sensor = _cache.GetSensor(SensorPathHelper.DecodeGuid(encodedId));

            var localValue = sensor is IBarSensor barSensor ? barSensor.LocalLastValue : null;

            return localValue?.ReceivingTime >= from && localValue?.ReceivingTime <= to ? localValue : null;
        }
    }
}