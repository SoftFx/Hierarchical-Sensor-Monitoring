using HSMSensorDataObjects;
using System.Collections.Generic;

namespace HSMDataCollector.Alerts
{
    public sealed class AlertAction<T> where T : AlertBuildRequest, new()
    {
        private readonly List<AlertConditionBuildRequest> _conditions;


        public SensorStatus Status { get; private set; } = SensorStatus.Ok;

        public string Template { get; private set; }

        public string Icon { get; private set; }


        public bool IsDisabled { get; private set; }


        internal AlertAction(List<AlertConditionBuildRequest> conditions)
        {
            _conditions = conditions;
        }


        public AlertAction<T> AndNotify(string template)
        {
            Template = template;

            return this;
        }

        public AlertAction<T> AndSetIcon(string icon)
        {
            Icon = icon;

            return this;
        }

        public AlertAction<T> AndSetSensorError()
        {
            Status = SensorStatus.Error;

            return this;
        }

        public T BuildAndDisable()
        {
            IsDisabled = true;

            return Build();
        }

        public T Build() => new T()
        {
            Conditions = _conditions,

            Template = Template,
            Status = Status,
            Icon = Icon,

            IsDisabled = IsDisabled
        };
    }
}