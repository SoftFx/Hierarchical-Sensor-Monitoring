using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    // The dump passwords of one product (ADR-0022 point 13): a row of its own in the configuration database, apart from
    // ProductEntity on purpose, so the value never reaches a product model, a view model, a change set or an API answer.
    //
    // Classes, not records: a record's generated ToString prints every property, the password included. ToString here
    // names the product and the versions only, so an entity that ends up in a log line or an exception text carries no value.
    public sealed class ProductDumpPasswordsEntity
    {
        public Guid ProductId { get; set; }

        public List<DumpPasswordVersionEntity> Versions { get; set; } = [];


        public override string ToString() => $"dump passwords of product {ProductId} ({Versions?.Count ?? 0} versions)";
    }


    public sealed class DumpPasswordVersionEntity
    {
        public int Version { get; set; }

        // Stored as it is: the archive builder needs the value itself.
        public string Password { get; set; }

        // UTC ticks.
        public long SetTicks { get; set; }

        public string SetBy { get; set; }

        // UTC ticks; null while an archive may still carry the version.
        public long? RetiredTicks { get; set; }


        public override string ToString() => $"dump password version {Version}";
    }
}
