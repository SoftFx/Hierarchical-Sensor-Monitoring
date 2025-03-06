using HSMCommon.Extensions;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace HSMServer.Core.Model.Policies
{
    public abstract class Policy
    {
        private AlertSystemTemplate _systemTemplate;
        private string _userTemplate;

        public List<PolicyCondition> Conditions { get; } = new();

        public Guid Id { get; private set; }


        internal SensorResult SensorResult { get; private set; } = SensorResult.Ok;

        internal PolicyResult PolicyResult { get; private set; } = PolicyResult.Ok;

        internal AlertState State { get; private set; }

        internal string Comment { get; private set; }


        public BaseSensorModel Sensor { get; private set; }


        public SensorStatus Status { get; private set; }

        public long? ConfirmationPeriod { get; private set; }

        public bool IsDisabled { get; private set; }

        public string Icon { get; private set; }


        public PolicyDestination Destination { get; set; } = new();

        public PolicySchedule Schedule { get; set; } = new();

        public Guid? TemplateId { get; set; }

        public string Template
        {
            get => _userTemplate;
            private set
            {
                if (_userTemplate == value)
                    return;

                _userTemplate = value;
                _systemTemplate = AlertState.BuildSystemTemplate(value);
            }
        }


        internal virtual bool UseScheduleManagerLogic => Schedule.IsActive;

        internal IPolicyDestinationHandler TargetChats
        {
            get
            {
                var chats = new Dictionary<Guid, string>();

                if (Destination.IsFromParentChats)
                {
                    foreach (var (id, name) in GetParentChats(Sensor?.Parent))
                    {
                        chats.TryAdd(id, name);
                    }
                }
                
                foreach (var (id, name) in Destination.Chats)
                {
                    chats.TryAdd(id, name);
                }

                return new PolicyDestinationHandler(chats,
                    (Sensor?.Parent?.Settings?.DefaultChats?.Value?.IsAllChats ?? false) && Destination.IsAllChats);
            }
        }

        internal Dictionary<Guid, string> GetParentChats(ProductModel parent)
        {
            var dict = new Dictionary<Guid, string>();

            if (parent is null)
                return dict;

            foreach (var (id, name) in parent.Settings.DefaultChats.CurValue.Chats)
            {
                dict.TryAdd(id, name);
            }

            if (parent.Settings.DefaultChats.CurValue.IsFromParent)
            {
                var par = parent.Parent;

                //TODO: Add folder chats when parent.FolderId.HasValue

                while (par != null)
                {
                    foreach (var (id, name) in GetParentChats(par))
                    {
                        dict.TryAdd(id, name);
                    }

                    par = par.Parent;
                }

                return dict;
            }

            return dict;
        }


        public Policy()
        {
            Id = Guid.NewGuid();
        }


        protected abstract AlertState GetState(BaseValue value);

        protected abstract PolicyCondition GetCondition(PolicyProperty property);


        public static Policy BuildPolicy(byte type) => type switch
        {
            (byte)SensorType.File => new FilePolicy(),
            (byte)SensorType.String => new StringPolicy(),
            (byte)SensorType.Boolean => new BooleanPolicy(),
            (byte)SensorType.Version => new VersionPolicy(),
            (byte)SensorType.TimeSpan => new TimeSpanPolicy(),
            (byte)SensorType.Integer => new IntegerPolicy(),
            (byte)SensorType.Double => new DoublePolicy(),
            (byte)SensorType.Rate => new RatePolicy(),
            (byte)SensorType.IntegerBar => new IntegerBarPolicy(),
            (byte)SensorType.DoubleBar => new DoubleBarPolicy(),
            (byte)SensorType.Enum => new EnumPolicy(),
            AlertTemplateModel.AnyType => new BooleanPolicy(),
            _ => null
        };


        public string RebuildState(PolicyCondition condition = null, BaseValue value = null)
        {
            if (Sensor is null)
                return string.Empty;

            State = GetState(value ?? Sensor.LastValue);
            State.Template = _systemTemplate;

            condition ??= Conditions?.FirstOrDefault();

            State.Operation = condition?.Operation.GetDisplayName();
            State.Property = condition?.Property.GetDisplayName();
            State.Target = condition?.Target.Value;

            Comment = State.BuildComment();

            PolicyResult = new PolicyResult(Sensor.Id, this);
            SensorResult = new SensorResult(Status, Comment);

            return Comment;
        }

        public Policy UpdatePolicy(PolicyUpdate update)
        {
            TryUpdate(update, out string error);
            return this;
        }

        public bool TryUpdate(PolicyUpdate update, out string error, BaseSensorModel sensor = null)
        {
            error = null;

            try
            {
                PolicyCondition Update(PolicyConditionUpdate update)
                {
                    var condition = BuildCondition(update.Property);

                    condition.Combination = update.Combination;
                    condition.Operation = update.Operation;
                    condition.Property = update.Property;

                    var target = update.Target;
                    if (target is not null && target.Type == TargetType.LastValue && target.Value is null)
                        target = update.Target with { Value = Sensor?.Id.ToString() };

                    condition.Target = target;

                    return condition;
                }

                Sensor ??= sensor;

                Destination.Update(update.Destination);
                ConfirmationPeriod = update.ConfirmationPeriod;
                Schedule.Update(update.Schedule);
                IsDisabled = update.IsDisabled;
                Template = update.Template;
                Status = update.Status;
                Icon = update.Icon;
                TemplateId = update.TemplateId;

                UpdateConditions(update.Conditions, Update);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            return string.IsNullOrEmpty(error);
        }

        public void Apply(PolicyEntity entity, BaseSensorModel sensor = null)
        {
            PolicyCondition Update(PolicyConditionEntity entity) => BuildCondition((PolicyProperty)entity.Property).FromEntity(entity);

            Sensor ??= sensor;

            Id = new Guid(entity.Id);
            try
            {
                TemplateId = entity.TemplateId?.Length > 0 ? new Guid(entity.TemplateId) : null;
            }
            catch
            {
                TemplateId = null;
            }
            Status = entity.SensorStatus.ToStatus();

            ConfirmationPeriod = entity.ConfirmationPeriod;
            IsDisabled = entity.IsDisabled;
            Template = entity.Template;
            Icon = entity.Icon;

            Destination = new PolicyDestination(entity.Destination);
            Schedule = new PolicySchedule(entity.Schedule);

            UpdateConditions(entity.Conditions, Update);
        }

        public PolicyEntity ToEntity() => new()
        {
            Id = Id.ToByteArray(),

            Conditions = Conditions?.Select(u => u.ToEntity()).ToList(),

            Destination = Destination.ToEntity(),
            Schedule = Schedule.ToEntity(),

            ConfirmationPeriod = ConfirmationPeriod,
            SensorStatus = (byte)Status,
            IsDisabled = IsDisabled,
            Template = Template,
            Icon = Icon,
            TemplateId = TemplateId.HasValue ? Id.ToByteArray() : []
        };


        public void ResetState()
        {
            Comment = string.Empty;

            SensorResult = SensorResult.Ok;
            PolicyResult = PolicyResult.Ok;
        }


        private PolicyCondition BuildCondition(PolicyProperty property)
        {
            BaseValue GetLastValue() => Sensor?.LastValue;

            return GetCondition(property).SetLastValueGetter(GetLastValue);
        }

        private void UpdateConditions<T>(List<T> updates, Func<T, PolicyCondition> updateHandler)
        {
            if (updates?.Count > 0)
            {
                Conditions.Clear();

                foreach (var update in updates)
                    Conditions.Add(updateHandler(update));
            }

            RebuildState();
        }


        public override string ToString()
        {
            var sb = new StringBuilder(1 << 5);

            sb.Append("If ");

            for (int i = 0; i < Conditions.Count; ++i)
            {
                var cond = Conditions[i];

                if (i > 0)
                    sb.Append($" {cond.Combination.GetDisplayName()}");

                sb.Append(cond);
            }

            return ActionsToString(sb).ToString();
        }

        protected StringBuilder ActionsToString(StringBuilder sb)
        {
            var actions = new List<string>();

            if (!string.IsNullOrEmpty(Template))
            {
                actions.Add($"send notifictaion with template={Template}");

                if (Destination is not null)
                    actions.Add($"to {Destination}");

                if (Schedule is not null)
                {
                    var schedule = UseScheduleManagerLogic ? Schedule.ToString() : Schedule.ToTtlString();

                    if (!string.IsNullOrEmpty(schedule))
                        actions.Add(schedule);
                }
            }

            if (!string.IsNullOrEmpty(Icon))
                actions.Add($"show icon={Icon}");

            if (!Status.IsOk())
                actions.Add($"change status to = {Status}");

            if (ConfirmationPeriod is not null)
                actions.Add($"after confirmation period={ConfirmationPeriod}");

            sb.Append($" then {string.Join(", ", actions)}");

            if (IsDisabled)
                sb.Append(" (disabled)");

            return sb;
        }
    }
}