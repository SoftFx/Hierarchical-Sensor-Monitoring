using HSMCommon.Extensions;

namespace HSMServer.Core.Model.Policies
{
    public abstract class Policy<T> : Policy where T : BaseValue
    {
        private AlertSystemTemplate _systemTemplate;
        private string _userTemplate;


        public override string Template
        {
            get => _userTemplate;
            protected set
            {
                if (_userTemplate == value)
                    return;

                _userTemplate = value;
                _systemTemplate = AlertState.BuildSystemTemplate(value);
            }
        }


        protected abstract AlertState GetState(T value, BaseSensorModel sensor);

        internal abstract bool Validate(T value, BaseSensorModel sensor);


        public string BuildStateAndComment(T value, BaseSensorModel sensor, PolicyCondition condition)
        {
            if (value is not null)
            {
                State = GetState(value, sensor);

                State.Operation = condition?.Operation.GetDisplayName();
                State.Target = condition?.Target.Value;

                State.Template = _systemTemplate;

                AlertComment = State.BuildComment();
            }
            else
                AlertComment = string.Empty;

            return AlertComment;
        }
    }
}