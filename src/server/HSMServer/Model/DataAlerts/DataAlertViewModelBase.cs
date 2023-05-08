using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace HSMServer.Model.DataAlerts
{
    public enum Operation
    {
        [Display(Name = "<=")]
        LessThanOrEqual,
        [Display(Name = "<")]
        LessThan,
        [Display(Name = ">")]
        GreatedThan,
        [Display(Name = ">=")]
        GreaterThanOrEqual,
    }


    public abstract class DataAlertViewModelBase
    {
        private readonly List<SensorStatus> _statuses = new() { SensorStatus.Error, SensorStatus.Warning };


        protected abstract List<string> Properties { get; }

        protected abstract List<Operation> Actions { get; }


        public Guid Id { get; set; }

        public string Property { get; set; }

        public Operation Action { get; set; }

        public string Value { get; set; }

        public SensorStatus Status { get; set; }

        public string Comment { get; set; }


        public List<SelectListItem> PropertiesItems => Properties.Select(p => new SelectListItem(p, p)).ToList();

        public List<SelectListItem> ActionsItems => Actions.Select(a => new SelectListItem(a.GetDisplayName(), $"{a}")).ToList();

        public List<SelectListItem> StatusesItems => _statuses.Select(s => new SelectListItem(s.GetDisplayName(), $"{s}")).ToList();


        public DataAlertViewModelBase() { }
    }
}
