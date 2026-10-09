using System;
using System.Collections.Generic;
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
    public sealed record FolderDto
    {
        /// <summary>Folder id.</summary>
        public Guid Id { get; init; }

        /// <summary>Folder display name (unique per server).</summary>
        public string Name { get; init; }

        /// <summary>Free-form description (may be empty).</summary>
        public string Description { get; init; }

        /// <summary>Folder color, ARGB (the web UI's color picker value).</summary>
        public int Color { get; init; }

        /// <summary>Folder creation time (UTC).</summary>
        public DateTime CreationDate { get; init; }

        /// <summary>Author display name; null when unknown (the id itself is never exposed).</summary>
        public string Author { get; init; }

        /// <summary>Products grouped into this folder (the template write path requires the folder to contain products).</summary>
        public List<FolderProductRefDto> Products { get; init; }

        /// <summary>
        /// Ids of the notification chats bound to the folder. The union of
        /// these chats and the GLOBAL chats (empty `folders` on a chat) is the
        /// chat set a policy or template in this folder may address.
        /// </summary>
        public List<Guid> Chats { get; init; }

        /// <summary>The folder's default-chats routing (what FromParent-destination policies resolve to).</summary>
        public FolderDefaultChatsDto DefaultChats { get; init; }

        /// <summary>Default sensor inactivity timeout for the folder's sensors (TimeInterval).</summary>
        public TimeIntervalDto Ttl { get; init; }

        /// <summary>Default sensor-history retention for the folder's sensors (TimeInterval).</summary>
        public TimeIntervalDto KeepHistory { get; init; }

        /// <summary>Remove-sensor-after-inactivity default for the folder's sensors (TimeInterval).</summary>
        public TimeIntervalDto SelfDestroy { get; init; }
    }


    /// <summary>A product grouped into a folder.</summary>
    public sealed record FolderProductRefDto
    {
        /// <summary>Product id (a root of the sensor tree).</summary>
        public Guid Id { get; init; }

        /// <summary>Product display name.</summary>
        public string Name { get; init; }
    }


    /// <summary>The default-chats routing of a folder: the destination a policy with a FromParent destination resolves to.</summary>
    public sealed record FolderDefaultChatsDto
    {
        /// <summary>Routing mode — "fromParent", "notInitialized", "empty" or "custom".</summary>
        public string Mode { get; init; }

        /// <summary>Selected chat ids; meaningful for the "custom" mode (and the persisted selection of "fromParent").</summary>
        public List<Guid> Chats { get; init; }
    }
}
