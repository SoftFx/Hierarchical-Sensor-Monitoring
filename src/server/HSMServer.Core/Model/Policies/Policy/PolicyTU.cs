namespace HSMServer.Core.Model.Policies
{
    public abstract class Policy<T, U> : Policy<T> where T : BaseValue
    {
        private BaseSensorModel _sensor;


        internal override bool Validate(T value, BaseSensorModel sensor)
        {
            _sensor ??= sensor;

            if (CheckConditions(value, out var failedCondition))
            {
                BuildStateAndComment(value, sensor, failedCondition);

                SensorResult = new SensorResult(Status, AlertComment);

                return false;
            }

            AlertComment = string.Empty;
            SensorResult = SensorResult.Ok;

            return true;
        }

        protected override PolicyCondition GetCondition() => new PolicyCondition<T, U>()
        {
            ConstTargetValueConverter = GetConstTarget,
            GetLastTargetValue = GetLastValue,
        };


        protected abstract U GetConstTarget(string strValue);

        private BaseValue GetLastValue() => _sensor?.LastValue;


        private bool CheckConditions(T value, out PolicyCondition failed)
        {
            failed = null;

            if (Conditions.Count > 0)
            {
                bool? fullResult = null;

                foreach (var condition in Conditions)
                {
                    var result = ((PolicyCondition<T, U>)condition).Check(value);

                    fullResult = fullResult is null
                        ? result
                        : condition.Combination switch
                        {
                            PolicyCombination.Or => fullResult | result,
                            _ => fullResult & result,
                        };

                    if (!fullResult.Value)
                        failed = condition;
                }

                return fullResult.Value;
            }

            return true;
        }
    }
}