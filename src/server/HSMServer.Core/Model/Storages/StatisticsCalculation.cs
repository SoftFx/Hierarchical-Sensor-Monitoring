using HSMServer.Core.Extensions;
using System.Numerics;

namespace HSMServer.Core.Model.Storages
{
    internal static class StatisticsCalculation
    {
        private const double EmaCoeff = 0.9;


        internal static T CalculateEma<T, U>(T previous, T current)
            where T : BaseValue<U>
            where U : struct, INumber<U> => current with
            {
                EmaValue = Calculate(previous?.EmaValue, current.Value),
            };

        internal static T CalculateBarEma<T, U>(T previous, T current)
            where T : BarBaseValue<U>
            where U : struct, INumber<U> => current with
            {
                EmaMin = Calculate(previous?.EmaMin, current.Min),
                EmaMax = Calculate(previous?.EmaMax, current.Max),
                EmaMean = Calculate(previous?.EmaMean, current.Mean),
                EmaCount = Calculate(previous?.EmaCount, current.Count),
            };


        private static double Calculate<T>(double? previous, T current) where T : struct, INumber<T>
        {
            var currentValue = double.CreateChecked(current);

            return previous.HasValue
                ? (EmaCoeff * double.CreateChecked(previous.Value) + (1 - EmaCoeff) * currentValue).Round()
                : currentValue;
        }
    }
}
