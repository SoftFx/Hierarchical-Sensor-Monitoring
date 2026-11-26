using System;
using System.Collections.Generic;
using HSMCommon.Model;

namespace HSMServer.Core.Model
{
    /// <summary>
    /// The value storage of a sensor that has no values: a table sensor (ADR-0021, #1844), whose rows live in blocks.
    /// Holds nothing and accepts nothing.
    /// </summary>
    internal sealed class NoValuesStorage : ValuesStorage
    {
        internal override BaseValue LastTimeout => null;

        internal override BaseValue LastDbValue => null;

        internal override BaseValue LastValue => null;

        internal override bool HasData => false;

        internal override DateTime From => DateTime.MinValue;

        internal override DateTime To => DateTime.MaxValue;


        internal override List<BaseValue> GetValues(DateTime from, DateTime to) => [];

        internal override List<BaseValue> GetValues(int count) => [];

        internal override bool TryChangeLastValue(BaseValue value) => false;

        internal override void Clear(DateTime to) { }

        internal override void Clear() { }

        internal override void Cut(DateTime time) { }

        internal override void WidenFrom(DateTime time) { }
    }
}
