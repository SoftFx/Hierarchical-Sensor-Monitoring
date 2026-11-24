using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Text;

namespace HSMTableBlocks
{
    /// <summary>One column of a <see cref="TableSchema"/>: its name and its type.</summary>
    public sealed class TableSchemaColumn
    {
        /// <summary>The column name, compared ordinally.</summary>
        public string Name { get; }

        /// <summary>The column type.</summary>
        public TableColumnType Type { get; }


        /// <summary>Creates a column; the rules are checked by <see cref="TableSchema"/>.</summary>
        public TableSchemaColumn(string name, TableColumnType type)
        {
            Name = name;
            Type = type;
        }
    }


    /// <summary>
    /// The schema of a table sensor (ADR-0021, point 2): an ordered list of named, typed columns, the first of which is
    /// the time. Its <see cref="Id"/> is the CRC-32 of its canonical bytes, so a collector and the server compute the
    /// same id without exchanging it. The canonical bytes and the rules are specified in
    /// <c>aicontext/features/api/table-block/feature.md</c>, section "Table schema"; changing either changes every id.
    /// <para>Immutable; safe from any thread.</para>
    /// </summary>
    public sealed class TableSchema
    {
        /// <summary>The version byte the canonical bytes start with.</summary>
        public const byte CanonicalVersion = 1;

        /// <summary>The longest column name, in UTF-8 bytes.</summary>
        public const int MaxNameLength = 255;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private readonly TableSchemaColumn[] _columns;
        private readonly TableColumnType[] _types;


        /// <summary>The columns in order; the first is the time.</summary>
        public IReadOnlyList<TableSchemaColumn> Columns => _columns;

        /// <summary>The column types in order, as a block's directory entry carries them.</summary>
        public IReadOnlyList<TableColumnType> ColumnTypes => _types;

        /// <summary>The schema id: CRC-32 of <see cref="CanonicalBytes"/>.</summary>
        public uint Id { get; }

        /// <summary>The canonical bytes the id is computed from (a copy).</summary>
        public byte[] CanonicalBytes => (byte[])_canonical.Clone();

        private readonly byte[] _canonical;


        /// <summary>
        /// Creates a schema and checks its rules: at least one column and at most <paramref name="limits"/>'
        /// <see cref="TableBlockLimits.MaxColumnsPerTable"/> (null: the defaults); the first column is
        /// <see cref="TableColumnType.Time"/>; every type is a known one; every name is non-empty, valid UTF-16, at
        /// most <see cref="MaxNameLength"/> UTF-8 bytes, free of control characters, and unique (ordinal).
        /// Throws <see cref="ArgumentException"/> naming the rule that failed.
        /// </summary>
        public TableSchema(IEnumerable<TableSchemaColumn> columns, TableBlockLimits limits = null)
        {
            if (columns == null)
                throw new ArgumentNullException(nameof(columns));

            limits ??= TableBlockLimits.Default;
            var list = new List<TableSchemaColumn>(columns);

            if (list.Count == 0)
                throw new ArgumentException("a table schema needs at least one column", nameof(columns));
            if (list.Count > limits.MaxColumnsPerTable)
                throw new ArgumentException($"a table schema has at most {limits.MaxColumnsPerTable} columns, this one has {list.Count}", nameof(columns));

            var names = new HashSet<string>(StringComparer.Ordinal);
            var encoded = new byte[list.Count][];
            int length = 2;

            for (int i = 0; i < list.Count; i++)
            {
                var column = list[i] ?? throw new ArgumentException($"column {i} is null", nameof(columns));

                if (!TableBlockFormat.IsKnownType((byte)column.Type))
                    throw new ArgumentException($"column {i} has unknown type {(int)column.Type}", nameof(columns));
                if (i == 0 && column.Type != TableColumnType.Time)
                    throw new ArgumentException("the first column of a table schema must be the time", nameof(columns));

                encoded[i] = EncodeName(column.Name, i);

                if (!names.Add(column.Name))
                    throw new ArgumentException($"column name '{column.Name}' is used twice", nameof(columns));

                length += 2 + encoded[i].Length;
            }

            _columns = list.ToArray();
            _types = new TableColumnType[_columns.Length];
            for (int i = 0; i < _types.Length; i++)
                _types[i] = _columns[i].Type;

            // Canonical bytes: version, column count, then per column its type, its name length and its UTF-8 name.
            _canonical = new byte[length];
            _canonical[0] = CanonicalVersion;
            _canonical[1] = (byte)_columns.Length;
            int at = 2;
            for (int i = 0; i < _columns.Length; i++)
            {
                _canonical[at++] = (byte)_columns[i].Type;
                _canonical[at++] = (byte)encoded[i].Length;
                encoded[i].CopyTo(_canonical, at);
                at += encoded[i].Length;
            }

            Id = Crc32.HashToUInt32(_canonical);
        }


        /// <summary>True when <paramref name="types"/> are exactly this schema's column types, in order.</summary>
        public bool HasColumnTypes(IReadOnlyList<TableColumnType> types)
        {
            if (types == null || types.Count != _types.Length)
                return false;

            for (int i = 0; i < _types.Length; i++)
                if (types[i] != _types[i])
                    return false;

            return true;
        }

        /// <summary>True when <paramref name="other"/> has the same columns: names (ordinal) and types, in order.</summary>
        public bool SameAs(TableSchema other)
        {
            if (other == null || other._canonical.Length != _canonical.Length)
                return false;

            for (int i = 0; i < _canonical.Length; i++)
                if (other._canonical[i] != _canonical[i])
                    return false;

            return true;
        }


        private static byte[] EncodeName(string name, int index)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException($"column {index} has no name", "columns");

            foreach (char c in name)
                if (char.IsControl(c))
                    throw new ArgumentException($"the name of column {index} contains a control character", "columns");

            byte[] bytes;
            try
            {
                bytes = StrictUtf8.GetBytes(name);
            }
            catch (EncoderFallbackException)
            {
                throw new ArgumentException($"the name of column {index} is not valid UTF-16 (a lone surrogate)", "columns");
            }

            if (bytes.Length > MaxNameLength)
                throw new ArgumentException($"the name of column {index} is {bytes.Length} UTF-8 bytes, at most {MaxNameLength} are allowed", "columns");

            return bytes;
        }
    }
}
