using HSMSensorDataObjects;
using System.Collections.Generic;

namespace HSMDataCollector.Alerts
{
    public class AlertAction<T> where T : AlertBaseTemplate, new()
    {
        private readonly List<AlertConditionTemplate> _conditions;


        public SensorStatus Status { get; private set; } = SensorStatus.Ok;

        public string Template { get; private set; }

        public string Icon { get; private set; }


        public bool IsDisabled { get; private set; }


        internal AlertAction(List<AlertConditionTemplate> conditions)
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

        public virtual T Build() => new T()
        {
            Conditions = _conditions,

            Template = Template,
            Status = Status,
            Icon = Icon,

            IsDisabled = IsDisabled
        };
    }
}