namespace HSMServer.Core.Model.Policies
{
    public abstract class Policy<T> : Policy where T : BaseValue
    {
        internal bool Validate(T value)
        {
            var prevStatus = IsActivate;
            var fail = CheckConditions(value, out var failedCondition);

            if (prevStatus != fail)
                IsActivate = fail;

            if (fail)
                RebuildState(failedCondition, value);
            else
                ResetState();

            return !fail;
        }


        private bool CheckConditions(T value, out PolicyCondition failed)
        {
            failed = null;

            if (Conditions.Count > 0)
            {
                bool? fullResult = null;

                foreach (var condition in Conditions)
                {
                    var result = ((IPolicyCondition<T>)condition).Check(value);

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