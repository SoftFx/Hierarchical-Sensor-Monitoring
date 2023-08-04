namespace HSMServer.Core.Model.Policies
{
    public abstract class Policy<T, U> : Policy<T> where T : BaseValue
    {
        internal override bool Validate(T value)
        {
            var fail = CheckConditions(value, out var failedCondition);

            if (fail)
                RebuildState(failedCondition, value);
            else
                ResetState();

            return !fail;
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