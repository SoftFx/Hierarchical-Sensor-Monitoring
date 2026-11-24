using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;
using HSMTableBlocks;

namespace HSMServer.Core.Model
{
    /// <summary>
    /// A table sensor (ADR-0021, #1844): its data is rows that arrive in blocks and are stored by the block store, not
    /// values. It has a schema fixed at registration and no values, statuses, alerts or comments: every value path
    /// refuses, and the members that would turn stored value bytes into a value throw, because no value of a table
    /// sensor is ever stored (the callers that reach them are refused before; see the feature doc).
    /// <para>
    /// Derives from the non-generic <see cref="BaseSensorModel"/> on purpose: the generic base needs a value type and
    /// a typed policy, and a table sensor has neither.
    /// </para>
    /// </summary>
    public sealed class TableSensorModel : BaseSensorModel
    {
        internal const string NoValuesError = "A table sensor has no values: its data is rows.";

        // The schema is read back with the format's own maximum, so a sensor registered under a looser
        // column limit than the one configured now still loads.
        private static readonly TableBlockLimits LoadLimits = new(maxColumnsPerTable: TableBlockLimits.FormatMaxColumnsPerTable);


        public override SensorType Type => SensorType.Table;

        public override SensorPolicyCollection Policies { get; }

        protected override ValuesStorage Storage { get; } = new NoValuesStorage();

        /// <summary>The schema fixed at registration.</summary>
        public TableSchema Schema { get; }

        /// <summary>The sequence number of the first block that may hold this sensor's rows (<see cref="TableSensorEntity.FirstSequence"/>).</summary>
        public long FirstSequence { get; }


        /// <summary>
        /// Builds the sensor from its entity. Throws <see cref="InvalidDataException"/> when the entity has no schema or
        /// its schema does not hold (the tree load catches it per sensor, logs it and leaves the entity untouched).
        /// </summary>
        public TableSensorModel(SensorEntity entity) : base(entity)
        {
            if (entity.Table is null)
                throw new InvalidDataException($"Table sensor {entity.Id} has no schema.");

            try
            {
                Schema = new TableSchema(entity.Table.Columns?.Select(c => new TableSchemaColumn(c.Name, (TableColumnType)c.Type))
                                         ?? Enumerable.Empty<TableSchemaColumn>(), LoadLimits);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException($"Table sensor {entity.Id} has an invalid schema: {ex.Message}", ex);
            }

            if (Schema.Id != entity.Table.SchemaId)
                throw new InvalidDataException($"Table sensor {entity.Id}: the stored schema id {entity.Table.SchemaId} is not the id of its columns ({Schema.Id}).");

            FirstSequence = entity.Table.FirstSequence;

            Policies = new TableSensorPolicyCollection();
            Policies.Attach(this);
        }


        internal static TableSensorEntity ToEntity(TableSchema schema, long firstSequence) => new()
        {
            SchemaId = schema.Id,
            Columns = schema.Columns.Select(c => new TableColumnEntity { Name = c.Name, Type = (byte)c.Type }).ToList(),
            FirstSequence = firstSequence,
        };

        internal override TableSensorEntity ToTableEntity() => ToEntity(Schema, FirstSequence);


        internal override bool IsHistoryLoaded => true;

        internal override bool HistoryLoadFailed => false;

        internal override bool HistoryRestoredByRetry => false;

        internal override HistoryLoadRetryResult RetryFailedHistoryLoad(DateTime utcNow) => HistoryLoadRetryResult.Suppressed;

        internal override void Initialize() { }

        internal override void RefreshAfterMerge() { }

        internal override void Revalidate() { }

        internal override bool CheckTimeout() => false;


        internal override bool TryAddValue(BaseValue value) => false;

        internal override bool TryAddValue(BaseValue value, out AddValueResult result)
        {
            result = AddValueResult.Cached;
            return false;
        }

        internal override bool TryUpdateLastValue(BaseValue value) => false;


        internal override BaseValue GetEmptyValue() => throw new NotSupportedException(NoValuesError);

        internal override IEnumerable<BaseValue> Convert(List<byte[]> valuesBytes) => throw new NotSupportedException(NoValuesError);

        internal override BaseValue Convert(byte[] bytes) => throw new NotSupportedException(NoValuesError);

        internal override BaseValue ConvertFromJson(string data) => throw new NotSupportedException(NoValuesError);
    }
}
