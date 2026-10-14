using System;
using System.Collections.Generic;
using System.ComponentModel;
using HSMServer.Model.ManagementApi.AlertTemplates;

namespace HSMServer.Model.ManagementApi.Folders
{
    /// <summary>
    /// An access-grouping folder — the entity alert templates are scoped to
    /// (`folderId`) and notification chats bind to. NOT the "folder" of
    /// `GET /api/v1/nodes` (that is a nested product in the tree's flat
    /// index): this is the separate grouping entity of the web UI's Folders
    /// page, and until this surface its ids were discoverable only
    /// indirectly, from the templates that referenced them.
    /// </summary>
    // Two-tier like ChatDto (the web UI's authorization model, which gates
    // the whole folder-detail page at the folder Manager):
    //  - id-discovery fields — id, name, products — answer every caller who
    //    can see the folder; products are FILTERED to the caller's own
    //    product sight, because a folder role must not grant product sight
    //    (per-product narrowing edits ProductsRoles only — the same
    //    no-folder-fallback rule the evaluator enforces);
    //  - detail fields (marked below) — null unless the token's owner is an
    //    admin or the folder's Manager.
    [Description("An access-grouping folder — the entity alert templates are scoped to (folderId) and notification chats bind to. NOT a tree node: the separate grouping entity of the web UI's Folders page.")]
    public sealed record FolderDto
    {
        /// <summary>Folder id.</summary>
        [Description("Folder id — the folderId alert templates reference.")]
        public Guid Id { get; init; }

        /// <summary>Folder display name (unique per server).</summary>
        [Description("Folder display name (unique per server).")]
        public string Name { get; init; }

        /// <summary>Free-form description (may be empty). Detail field: null unless the owner is an admin or the folder's manager.</summary>
        [Description("Free-form description (may be empty). Detail field: null unless the owner is an admin or the folder's manager.")]
        public string Description { get; init; }

        /// <summary>Folder color, ARGB (the web UI's color picker value). Detail field: null unless the owner is an admin or the folder's manager.</summary>
        [Description("Folder color, ARGB (the web UI's color picker value). Detail field: null unless the owner is an admin or the folder's manager.")]
        public int? Color { get; init; }

        /// <summary>Folder creation time (UTC). Detail field: null unless the owner is an admin or the folder's manager.</summary>
        [Description("Folder creation time (UTC). Detail field: null unless the owner is an admin or the folder's manager.")]
        public DateTime? CreationDate { get; init; }

        /// <summary>Author display name (detail field: null unless the owner is an admin or the folder's manager; the id itself is never exposed).</summary>
        [Description("Author display name (detail field: null unless the owner is an admin or the folder's manager; the id itself is never exposed).")]
        public string Author { get; init; }

        /// <summary>
        /// Products grouped into this folder that the CALLER CAN SEE (the
        /// template write path requires the folder to contain products). A
        /// product the owner was explicitly revoked from stays absent — a
        /// folder role never grants product sight.
        /// </summary>
        [Description("Products grouped into this folder that the CALLER CAN SEE (the template write path requires the folder to contain products).")]
        public List<FolderProductRefDto> Products { get; init; }

        /// <summary>Ids of the notification chats bound to the folder. Detail field: null unless the owner is an admin or the folder's manager.</summary>
        [Description("Ids of the notification chats bound to the folder. Detail field: null unless the owner is an admin or the folder's manager.")]
        public List<Guid> Chats { get; init; }

        /// <summary>The folder's default-chats routing (what FromParent-destination policies resolve to). Detail field: null unless the owner is an admin or the folder's manager.</summary>
        [Description("The folder's default-chats routing (what FromParent-destination policies resolve to). Detail field: null unless the owner is an admin or the folder's manager.")]
        public FolderDefaultChatsDto DefaultChats { get; init; }

        /// <summary>Default sensor inactivity timeout for the folder's sensors (TimeInterval). Detail field: null unless the owner is an admin or the folder's manager.</summary>
        [Description("Default sensor inactivity timeout for the folder's sensors. Detail field: null unless the owner is an admin or the folder's manager.")]
        public TimeIntervalDto Ttl { get; init; }

        /// <summary>Default sensor-history retention for the folder's sensors (TimeInterval). Detail field: null unless the owner is an admin or the folder's manager.</summary>
        [Description("Default sensor-history retention for the folder's sensors. Detail field: null unless the owner is an admin or the folder's manager.")]
        public TimeIntervalDto KeepHistory { get; init; }

        /// <summary>Remove-sensor-after-inactivity default for the folder's sensors (TimeInterval). Detail field: null unless the owner is an admin or the folder's manager.</summary>
        [Description("Remove-sensor-after-inactivity default for the folder's sensors. Detail field: null unless the owner is an admin or the folder's manager.")]
        public TimeIntervalDto SelfDestroy { get; init; }
    }


    /// <summary>A product grouped into a folder.</summary>
    [Description("A product grouped into a folder.")]
    public sealed record FolderProductRefDto
    {
        /// <summary>Product id (a root of the sensor tree).</summary>
        [Description("Product id (a root of the sensor tree).")]
        public Guid Id { get; init; }

        /// <summary>Product display name.</summary>
        [Description("Product display name.")]
        public string Name { get; init; }
    }


    /// <summary>The default-chats routing of a folder: the destination a policy with a FromParent destination resolves to.</summary>
    [Description("The default-chats routing of a folder: the destination a policy with a FromParent destination resolves to.")]
    public sealed record FolderDefaultChatsDto
    {
        /// <summary>Routing mode — "fromParent", "notInitialized", "empty" or "custom".</summary>
        [Description("Routing mode — \"fromParent\", \"notInitialized\", \"empty\" or \"custom\".")]
        public string Mode { get; init; }

        /// <summary>Selected chat ids; meaningful for the "custom" mode (and the persisted selection of "fromParent").</summary>
        [Description("Selected chat ids; meaningful for the \"custom\" mode (and the persisted selection of \"fromParent\").")]
        public List<Guid> Chats { get; init; }
    }
}
