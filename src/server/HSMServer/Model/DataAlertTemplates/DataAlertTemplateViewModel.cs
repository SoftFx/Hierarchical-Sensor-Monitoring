using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.DataAlerts;
using HSMServer.Model.Folders;
using Microsoft.AspNetCore.Mvc.Rendering;


namespace HSMServer.Model.DataAlertTemplates
{
    public sealed class DataAlertTemplateViewModel
    {
        public const byte AnyType = AlertTemplateModel.AnyType;


        public Guid Id { get; set; }

        public string EncodedId => Id.ToString();

        [Required]
        public string Name { get; set; }

        [Required]
        public string PathTemplate { get; set; }

        public List<BaseSensorModel> Sensors { get; set; }

        public byte Type { get; set; } = AnyType;

        public Dictionary<byte, List<DataAlertViewModelBase>> DataAlerts { get; set; } = [];

        [DisplayName("Folder")]
        public Guid? FolderId {  get; set; }

        public SelectList AvailableFolders { get; set; }

        public bool HasTimeToLive => DataAlerts.ContainsKey(TimeToLiveAlertViewModel.AlertKey);

        public bool IsNew { get; set; } = true;

        public DataAlertTemplateViewModel()
        {
            Id = Guid.NewGuid();
        }

        public DataAlertTemplateViewModel(AlertTemplateModel model)
        {
            Id = model.Id;
            Name = model.Name;
            PathTemplate = model.Path;
            Type = model.SensorType;
            FolderId = model.FolderId;

            IsNew = false;

            byte type = (byte)(Type > 0 ? Type : 0);

            DataAlerts = [];

            if (model.TTLPolicy != null)
            {
                var interval = new TimeIntervalViewModel().FromModel(model.TTL, PredefinedIntervals.ForTimeout);
                var ttl = new TimeToLiveAlertViewModel(model.TTLPolicy, interval) { IsModify = true };
                DataAlerts[TimeToLiveAlertViewModel.AlertKey] = [ttl];
            }


            if(model.Policies.Count > 0)
                DataAlerts.Add(type, model.Policies.Select(x => { var result = DataAlertViewModel.BuildAlert(x); result.IsModify = true; return result; }).ToList());
        }

        public DataAlertTemplateViewModel(AlertTemplateModel model, IEnumerable<FolderModel> availableFolders) : this(model)
        {
            SetAvailableFolders(availableFolders);
        }

        public DataAlertTemplateViewModel(IEnumerable<FolderModel> availableFolders) : this()
        {
            SetAvailableFolders(availableFolders);
        }

        public AlertTemplateModel ToModel()
        {
            AlertTemplateModel result = new AlertTemplateModel()
            {
                Id = Id,
                Name = Name,
                Path = PathTemplate,
                SensorType = Type,
                FolderId = FolderId,
            };

            result.TryApplyPathTemplate(PathTemplate, out _);

            var ttl = DataAlerts.TryGetValue(TimeToLiveAlertViewModel.AlertKey, out var alerts) && alerts.Count > 0 ? alerts[0] : null;

            if (ttl != null)
            {
                if (ttl.Id == Guid.Empty)
                    ttl.Id = Guid.NewGuid();

                result.TTLPolicy = new TTLPolicy();
                var update = ttl.ToTimeToLiveUpdate(InitiatorInfo.AlertTemplate, []);
                result.TTLPolicy.FullUpdate(update);
                result.TTL = ttl.Conditions?[0].TimeToLive.ToModel() ?? TimeIntervalModel.None;
            }

            byte key = DataAlerts.Keys.FirstOrDefault(x => x != TimeToLiveAlertViewModel.AlertKey);

            if (DataAlerts.TryGetValue(key, out alerts) && alerts != null)
            {
                if (key == AnyType)
                    key = (byte)SensorType.Boolean;

                foreach (var item in alerts)
                {
                    if (item.Id == Guid.Empty)
                        item.Id = Guid.NewGuid();

                    var policy = Policy.BuildPolicy(key);
                    var update = item.ToUpdate([]);
                    policy.UpdatePolicy(update);
                    result.Policies.Add(policy);
                }
            }

            return result;
        }

        private void SetAvailableFolders(IEnumerable<FolderModel> availableFolders)
        {
            AvailableFolders = new SelectList(availableFolders, "Id", "Name", FolderId);
        }
    }
}