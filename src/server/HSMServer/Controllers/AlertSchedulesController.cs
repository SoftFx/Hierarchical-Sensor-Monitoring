using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HSMServer.ApiObjectsConverters;
using HSMServer.Authentication;
using HSMServer.Core.DataLayer;
using HSMServer.Model.AlertSchedule;
using System.Collections.Generic;
using HSMServer.Core.Schedule;



namespace HSMServer.Controllers
{
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class AlertSchedulesController : BaseController
    {
        private static readonly JsonSerializerOptions _serializeOptions = new()
        {
            WriteIndented = true,
        };

        private static readonly JsonSerializerOptions _deserializeOptions = new()
        {
            AllowTrailingCommas = true,
        };

        private readonly IAlertScheduleProvider _scheduleProvider;
        private readonly AlertScheduleParser _parser = new();

        static AlertSchedulesController()
        {
            _deserializeOptions.Converters.Add(new JsonStringEnumConverter());

            _serializeOptions.Converters.Add(new ListAsJsonStringConverter());
            _serializeOptions.Converters.Add(new JsonStringEnumConverter());
        }

        public AlertSchedulesController(IAlertScheduleProvider scheduleProvider, IUserManager users) : base(users)
        {
            _scheduleProvider = scheduleProvider;
        }

        [HttpGet]
        public IActionResult Index()
        {
            List<AlertScheduleViewModel> result = _scheduleProvider.GetAllSchedules().Select(x => new AlertScheduleViewModel(x)).ToList() ?? [];

            return View(result);
        }


        //[HttpGet]
        //public IActionResult New()
        //{
        //    return View("AlertSchedule", new AlertScheduleViewModel());
        //}

        //[HttpGet]
        //public IActionResult Edit(Guid id)
        //{

        //    var data = _scheduleProvider.GetSchedule(id);

        //    return View("AlertSchedule", new AlertScheduleViewModel(data));
        //}

        //[HttpPost]
        //public IActionResult Save(AlertScheduleViewModel model)
        //{
        //    if (model.Id == Guid.Empty)
        //        model.Id = Guid.NewGuid();

        //    try
        //    {
        //        var schedule = _parser.Parse(model.Schedule);

        //        schedule.Id = model.Id;
        //        schedule.Name = model.Name;
        //        schedule.Timezone = model.Timezone;

        //        _scheduleProvider.SaveSchedule(schedule);

        //        return RedirectToAction("Index");
        //    }
        //    catch (Exception ex)
        //    {
        //        ModelState.AddModelError("Schedule", ex.Message);
        //        return View("AlertSchedule", model);
        //    }
        //}

        [HttpGet]
        public IActionResult Remove(Guid id)
        {
            _scheduleProvider.DeleteSchedule(id);

            return RedirectToAction("Index");
        }

        [HttpGet]
        public IActionResult NewPartial()
        {
            return PartialView("_AlertSchedule", new AlertScheduleViewModel());
        }

        [HttpGet]
        public IActionResult EditPartial(Guid id)
        {
            var data = _scheduleProvider.GetSchedule(id);
            if (data == null)
                return NotFound();

            return PartialView("_AlertSchedule", new AlertScheduleViewModel(data));
        }

        [HttpPost]
        public IActionResult SavePartial(AlertScheduleViewModel model)
        {
            if (ModelState.IsValid)
            {
                if (model.Id == Guid.Empty)
                    model.Id = Guid.NewGuid();

                try
                {
                    var schedule = _parser.Parse(model.Schedule);

                    schedule.Id = model.Id;
                    schedule.Name = model.Name;
                    schedule.Timezone = model.Timezone;

                    _scheduleProvider.SaveSchedule(schedule);

                    var list = _scheduleProvider.GetAllSchedules().Select(x => new AlertScheduleViewModel(x)).ToList();
                    return PartialView("_AlertSchedulesTable", list);
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("Schedule", ex.Message);
                    return PartialView("_AlertSchedule", model);
                }
            }

            return PartialView("_AlertSchedule", model);
        }

        [HttpGet]
        public IActionResult GetAlertSchedulesTable()
        {
            var list = _scheduleProvider.GetAllSchedules().Select(x => new AlertScheduleViewModel(x)).ToList();
            return PartialView("_AlertSchedulesTable", list);
        }
    }
}