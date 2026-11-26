using System;

namespace HSMServer.Authentication
{
    // Authorization target of a management-API call. A Sensor is deliberately not an
    // independently selectable scope: it inherits the Product boundary resolved from the
    // live hierarchy at evaluation time (its product's CURRENT folder included). A Chat is
    // a target of its own only for AuthorizeChatWrite — it resolves no boundary (the
    // generic AuthorizeRead/Write answer it NotFound, fail closed): its sight is a
    // composition over the folders it is bound to.
    public enum ApiTokenResourceKind
    {
        Global = 0,
        Product = 1,
        Folder = 2,
        Sensor = 3,
        Chat = 4,
    }

    public sealed record ApiTokenResource(ApiTokenResourceKind Kind, Guid Id = default)
    {
        public static readonly ApiTokenResource GlobalScope = new(ApiTokenResourceKind.Global);

        public static ApiTokenResource Product(Guid id) => new(ApiTokenResourceKind.Product, id);

        public static ApiTokenResource Folder(Guid id) => new(ApiTokenResourceKind.Folder, id);

        public static ApiTokenResource Sensor(Guid id) => new(ApiTokenResourceKind.Sensor, id);

        public static ApiTokenResource Chat(Guid id) => new(ApiTokenResourceKind.Chat, id);
    }

    // Outcome with the documented HTTP split baked in: Allowed; Forbidden (boundary
    // covered and owner can see the target, but the operation is not granted or the owner
    // currently cannot perform it); NotFound (target absent, invisible to the owner, or no
    // token grant covers its current boundary — the anti-enumeration answer).
    public enum ApiTokenAuthorization
    {
        Allowed = 0,
        Forbidden = 1,
        NotFound = 2,
    }
}
