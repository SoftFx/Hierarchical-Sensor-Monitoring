using HSMCommon.Extensions;

namespace HSMServer.Core.Model.Policies
{
    public abstract class Policy<T> : Policy where T : BaseValue
    {
        protected abstract AlertState GetState(T value, BaseSensorModel sensor);

        internal abstract bool Validate(T value, BaseSensorModel sensor);


        public override string BuildStateAndComment(BaseValue value, BaseSensorModel sensor, PolicyCondition condition)
        {
            if (value is T valueT)
            {
                var state = GetState(valueT, sensor);

                state.Operation = condition?.Operation.GetDisplayName();
                state.Target = condition?.Target.Value;

                return SetStateAndGetComment(state);
            }

            AlertComment = string.Empty;

            return AlertComment;
        }
    }
}