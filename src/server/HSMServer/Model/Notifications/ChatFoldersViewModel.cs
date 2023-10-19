using HSMServer.Extensions;
using HSMServer.Model.Folders;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.Notifications
{
    public class ChatFoldersViewModel
    {
        public List<FolderModel> DisplayFolders { get; } = new();

        public List<SelectListItem> AvailableFolders { get; }

        public List<Guid> SelectedFolders { get; set; } = new();

        public List<Guid> Folders { get; set; } = new();


        public ChatFoldersViewModel() { }

        public ChatFoldersViewModel(List<FolderModel> availableFolders, List<FolderModel> chatFolders)
        {
            AvailableFolders = availableFolders.ToSelectedItems(f => f.Name, f => f.Id.ToString()).OrderBy(p => p.Text).ToList();

            DisplayFolders.AddRange(chatFolders.OrderBy(p => p.Name));
            Folders = DisplayFolders.Select(p => p.Id).ToList();
        }
    }
}
