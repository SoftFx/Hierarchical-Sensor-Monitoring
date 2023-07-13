using System;

namespace HSMServer.Core.Model.Policies
{
    public abstract class Policy<T, U> : Policy<T> where T : BaseValue
    {
        protected abstract Func<U, U, bool> GetOperation(PolicyOperation operation);

        protected abstract Func<T, U> GetProperty(string property);

        protected abstract U GetConstTarget(string strValue);


        internal override bool Validate(T value, BaseSensorModel sensor)
        {
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
            OperationBuilder = GetOperation,
            PropertyBuilder = GetProperty,
            TargetBuilder = GetConstTarget,
        };

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