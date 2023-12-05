﻿using System.Numerics;

namespace HSMServer.Core.Model.Storages
{
    internal static class StatisticsCalculation
    {
        private const double EmaCoeff = 0.9;


        internal static T CalculateEma<T, U>(T previous, T current)
            where T : BaseValue<U>
            where U : struct, INumber<U> => current with
            {
                EmaValue = Calculate(previous?.Value, current.Value),
            };

        internal static T CalculateBarEma<T, U>(T previous, T current)
            where T : BarBaseValue<U>
            where U : struct, INumber<U> => current with
            {
                EmaMin = Calculate(previous?.Min, current.Min),
                EmaMax = Calculate(previous?.Max, current.Max),
                EmaMean = Calculate(previous?.Mean, current.Mean),
                EmaCount = Calculate(previous?.Count, current.Count),
            };


        private static double Calculate<T>(T? previous, T current) where T : struct, INumber<T>
        {
            var currentValue = double.CreateChecked(current);

            return previous.HasValue
                ? EmaCoeff * double.CreateChecked(previous.Value) + (1 - EmaCoeff) * currentValue
                : currentValue;
        }
    }
}
