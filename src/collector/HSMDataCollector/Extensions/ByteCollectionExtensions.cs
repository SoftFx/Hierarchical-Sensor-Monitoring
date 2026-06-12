using System.Collections.Generic;
using System.Reflection;

namespace HSMDataCollector.Extensions
{
    /// <summary>
    /// The file-sensor wire DTO requires <see cref="List{T}"/> of bytes, but building one from the
    /// read buffer doubles the transient allocation for every sent file (#1102-C1). This wraps the
    /// buffer into a List without copying by assigning the List's backing array directly; if the
    /// runtime's List internals ever change shape, it silently falls back to the copying constructor.
    /// The buffer must not be mutated after wrapping — callers hand over ownership.
    /// </summary>
    internal static class ByteCollectionExtensions
    {
        private static readonly FieldInfo ItemsField = typeof(List<byte>).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SizeField = typeof(List<byte>).GetField("_size", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static readonly bool ZeroCopySupported = ItemsField != null && SizeField != null;

        internal static List<byte> AsList(this byte[] bytes)
        {
            if (!ZeroCopySupported || bytes.Length == 0)
                return new List<byte>(bytes);

            var list = new List<byte>();

            ItemsField.SetValue(list, bytes);
            SizeField.SetValue(list, bytes.Length);

            return list;
        }
    }
}
