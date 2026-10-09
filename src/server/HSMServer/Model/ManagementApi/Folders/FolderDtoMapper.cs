using System;
using System.Linq;
using HSMServer.Model.Controls;
using HSMServer.Model.Folders;
using HSMServer.Model.ManagementApi.AlertTemplates;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;

namespace HSMServer.Model.ManagementApi.Folders
{
    // Entity -> DTO for the folder read surface. Credential-free: the
    // visibility decision lives in FoldersReadService, not here.
    public static class FolderDtoMapper
    {
        public static FolderDto ToDto(FolderModel folder)
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
                Description = folder.Description,
                Color = folder.Color.ToArgb(),
                CreationDate = folder.CreationDate,
                Author = folder.Author,
                Products = StableCopy.Of(() => folder.Products.Values)
                    .OrderBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(product => product.Id)
                    .Select(product => new FolderProductRefDto { Id = product.Id, Name = product.Name })
                    .ToList(),
                Chats = StableCopy.Of(() => folder.Chats),
                DefaultChats = new FolderDefaultChatsDto
                {
                    Mode = ModeName(defaultChats?.ChatMode),
                    Chats = [.. (defaultChats is null ? [] : StableCopy.Of(() => defaultChats.SelectedChats))],
                },
                Ttl = ToIntervalDto(folder.TTL),
                KeepHistory = ToIntervalDto(folder.KeepHistory),
                SelfDestroy = ToIntervalDto(folder.SelfDestroy),
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
