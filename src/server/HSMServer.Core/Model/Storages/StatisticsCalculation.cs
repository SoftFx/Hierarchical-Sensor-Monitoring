using HSMServer.Core.Extensions;
using System.Numerics;

namespace HSMServer.Core.Model.Storages
{
    internal static class StatisticsCalculation
    {
        private const double NewEmaCoef = 1 - OldEmaCoef;
        private const double OldEmaCoef = 0.9;


        internal static T CalculateEma<T, U>(T previous, T current)
            where T : BaseValue<U>
            where U : struct, INumber<U> => current with
            {
                EmaValue = CalculateEMA(previous?.EmaValue, current.Value),
            };

        internal static T RecalculateEma<T, U>(T previous, T current)
            where T : BaseValue<U>
            where U : struct, INumber<U> => current with
            {
                EmaValue = RecalculateEMA(previous?.EmaValue, previous?.Value, current.Value),
            };

        internal static T CalculateBarEma<T, U>(T previous, T current)
            where T : BarBaseValue<U>
            where U : struct, INumber<U> => current with
            {
                EmaMin = CalculateEMA(previous?.EmaMin, current.Min),
                EmaMax = CalculateEMA(previous?.EmaMax, current.Max),
                EmaMean = CalculateEMA(previous?.EmaMean, current.Mean),
                EmaCount = CalculateEMA(previous?.EmaCount, current.Count),
            };

        internal static T RecalculateBarEma<T, U>(T previous, T current)
            where T : BarBaseValue<U>
            where U : struct, INumber<U> => current with
            {
                EmaMin = RecalculateEMA(previous?.EmaMin, previous?.Min, current.Min),
                EmaMax = RecalculateEMA(previous?.EmaMax, previous?.Max, current.Max),
                EmaMean = RecalculateEMA(previous?.EmaMean, previous?.Mean, current.Mean),
                EmaCount = RecalculateEMA(previous?.EmaCount, previous?.Count, current.Count),
            };


        private static double CalculateEMA<T>(double? curValue, T newRaw) where T : struct, INumber<T>
        {
            var newValue = GetNumber(newRaw);

            return (curValue.HasValue ? OldEmaCoef * curValue.Value + NewEmaCoef * newValue : NewEmaCoef * newValue).Round();
        }

        private static double RecalculateEMA<T>(double? curEma, T? curRaw, T newRaw) where T : struct, INumber<T>
        {
            var newValue = GetNumber(newRaw);
            var curValue = curRaw.HasValue ? GetNumber(curRaw.Value) : default;

            return (curEma.HasValue && curRaw.HasValue ? curEma.Value - NewEmaCoef * (curValue - newValue) : NewEmaCoef * newValue).Round();
        }

        private static double GetNumber<T>(T value) where T : INumber<T> => double.CreateChecked(value);
    }
}
