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

        private readonly IDatabaseCore _database;

        static AlertSchedulesController()
        {
            _deserializeOptions.Converters.Add(new JsonStringEnumConverter());

            _serializeOptions.Converters.Add(new ListAsJsonStringConverter());
            _serializeOptions.Converters.Add(new JsonStringEnumConverter());
        }

        public AlertSchedulesController(IDatabaseCore database, IUserManager users) : base(users)
        {
            _database = database;
        }

        [HttpGet]
        public IActionResult Index()
        {
            List<AlertScheduleViewModel> result = _database.GetAllAlertSchedules().Select(x => new AlertScheduleViewModel(x)).ToList() ?? [];

            return View(result);
        }


        [HttpGet]
        public IActionResult New()
        {
            return View("AlertSchedule", new AlertScheduleViewModel());
        }

        [HttpGet]
        public IActionResult Edit(Guid id)
        {

            var data = _database.GetAlertSchedule(id);

            return View("AlertSchedule", new AlertScheduleViewModel(data));
        }

        [HttpPost]
        public IActionResult Save(AlertScheduleViewModel model)
        {
            if (model.Id == Guid.Empty)
                model.Id = Guid.NewGuid();

            var list = _database.GetAllAlertSchedules();

            var entity = new HSMDatabase.AccessManager.DatabaseEntities.AlertScheduleEntity()
            {
                Id = model.Id.ToByteArray(),
                Name = model.Name,
                TimeZone = model.TimeZone,
                Schedule = model.Schedule,
            };

            _database.AddAlertSchedule(entity);

            return RedirectToAction("Index");
        }

        [HttpGet]
        public IActionResult Remove(Guid id)
        {
            _database.RemoveAlertSchedule(id);

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
            var data = _database.GetAlertSchedule(id);
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

                var entity = new HSMDatabase.AccessManager.DatabaseEntities.AlertScheduleEntity()
                {
                    Id = model.Id.ToByteArray(),
                    Name = model.Name,
                    TimeZone = model.TimeZone,
                    Schedule = model.Schedule,
                };

                _database.AddAlertSchedule(entity); 

                var list = _database.GetAllAlertSchedules().Select(x => new AlertScheduleViewModel(x)).ToList();
                return PartialView("_AlertSchedulesTable", list);
            }

            return PartialView("_AlertSchedule", model);
        }

        [HttpGet]
        public IActionResult GetAlertSchedulesTable()
        {
            var list = _database.GetAllAlertSchedules().Select(x => new AlertScheduleViewModel(x)).ToList();
            return PartialView("_AlertSchedulesTable", list);
        }
    }
}