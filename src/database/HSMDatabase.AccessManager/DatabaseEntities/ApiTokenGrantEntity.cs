namespace HSMDatabase.AccessManager.DatabaseEntities
{
    // Authorization boundary a token grant is bound to. Global covers global operations
    // only; Product and Folder store a stable resource id and follow the current hierarchy
    // membership. Values other than these must fail closed during deserialization/authorization.
    public enum ApiTokenBoundaryKind : byte
    {
        Global = 0,

        Product = 1,

        Folder = 2,
    }


    // One explicit operation + boundary pair of an API token grant. Operation is a canonical
    // permission name from the management API catalog; the pair is never recombinable with
    // pairs of other tokens or grants, and the boundary is always a concrete resource id
    // (wildcards are forbidden and must be rejected before persistence).
    public sealed record ApiTokenGrantEntity
    {
        public string Operation { get; init; }

        public byte BoundaryKind { get; init; }

        // Stable Product/Folder id for the Product/Folder kinds; must be empty for Global.
        public string BoundaryId { get; init; }
    }
}
