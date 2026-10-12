using System;
using System.Linq;
using HSMServer.Model.Controls;
using HSMServer.Model.Folders;
using HSMServer.Model.ManagementApi.AlertTemplates;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;

namespace HSMServer.Model.ManagementApi.Folders
{
    // Entity -> DTO for the folder read surface. Credential-free: the
    // visibility and detail decisions live in FoldersReadService, not here —
    // the service passes the product-sight predicate and the detail flag in,
    // so the mapper stays usable from any context (the chats mapper follows
    // the same split).
    public static class FolderDtoMapper
    {
        public static FolderDto ToDto(FolderModel folder, Func<Guid, bool> isProductVisible, bool includeDetail)
        {
            // The product/chat/default-chats collections mutate from web-UI
            // edits without a lock — snapshot them (StableCopy) before the
            // mapping enumerates; DefaultChats is captured to a local because
            // folder updates replace the reference wholesale.
            var defaultChats = folder.DefaultChats;

            return new FolderDto
            {
                Id = folder.Id,
                Name = folder.Name,

                // Filtered to the caller's product sight: a folder role must
                // not grant product sight — a product the owner was revoked
                // from stays absent, exactly like the tree's GetUserProducts
                // rule (the evaluator's no-folder-fallback rationale).
                Products = StableCopy.Of(() => folder.Products.Values)
                    .Where(product => isProductVisible(product.Id))
                    .OrderBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(product => product.Id)
                    .Select(product => new FolderProductRefDto { Id = product.Id, Name = product.Name })
                    .ToList(),

                // Detail fields — null unless the owner is an admin or the
                // folder's manager (the web UI's folder-page gate); the
                // fields every sighted caller gets are the id-discovery half
                // the template writes validate against.
                Description = includeDetail ? folder.Description : null,
                Color = includeDetail ? folder.Color.ToArgb() : null,
                CreationDate = includeDetail ? folder.CreationDate : null,
                Author = includeDetail ? folder.Author : null,
                Chats = includeDetail ? StableCopy.Of(() => folder.Chats) : null,
                DefaultChats = includeDetail ? new FolderDefaultChatsDto
                {
                    Mode = ModeName(defaultChats?.ChatMode),
                    Chats = [.. (defaultChats is null ? [] : StableCopy.Of(() => defaultChats.SelectedChats))],
                } : null,
                Ttl = includeDetail ? ToIntervalDto(folder.TTL) : null,
                KeepHistory = includeDetail ? ToIntervalDto(folder.KeepHistory) : null,
                SelfDestroy = includeDetail ? ToIntervalDto(folder.SelfDestroy) : null,
            };
        }


        // The same sparse-enum contract the template TTL intervals use (see
        // TimeIntervalDto): Interval is the authoritative core enum value of
        // the STORED setting (the view model's own ToModel() — its storage
        // serialization path), Ticks the span when Interval is -1 (Ticks).
        private static TimeIntervalDto ToIntervalDto(TimeIntervalViewModel model)
        {
            var stored = model?.ToModel();

            return new TimeIntervalDto
            {
                Interval = (long)(stored?.Interval ?? CoreTimeInterval.None),
                Ticks = stored?.Ticks ?? 0L,
            };
        }


        private static string ModeName(DefaultChatMode? mode) => mode switch
        {
            DefaultChatMode.FromParent => "fromParent",
            DefaultChatMode.NotInitialized => "notInitialized",
            DefaultChatMode.Empty => "empty",
            DefaultChatMode.Custom => "custom",
            _ => null,
        };
    }
}
