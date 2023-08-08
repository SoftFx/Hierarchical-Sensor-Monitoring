﻿using HSMServer.Attributes;
using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Linq;
using HSMServer.Core.Cache;

namespace HSMServer.Model.Folders.ViewModels
{
    public sealed class EditFolderViewModel
    {
        public TelegramSettingsViewModel Telegram { get; }

        public FolderCleanupViewModel Cleanup { get; }

        public FolderAlertsViewModel Alerts { get; }

        public FolderUsersViewModel Users { get; }

        public string CreationDate { get; }

        public string Author { get; }


        public FolderProductsViewModel Products { get; set; }

        public Guid Id { get; set; }

        [Required(ErrorMessage = "{0} is required.")]
        [StringLength(60, ErrorMessage = "{0} length should be less than {1}.")]
        [UniqueValidation(ErrorMessage = "Folder with the same name already exists.")]
        public string Name { get; set; }

        public string OldName { get; set; }

        public string Description { get; set; }

        public Color Color { get; set; }


        public bool IsAddMode => Id == default;

        public bool IsNameChanged => Name != OldName;


        public EditFolderViewModel() { }

        public EditFolderViewModel(FolderProductsViewModel products)
        {
            Products = products;
        }

        internal EditFolderViewModel(FolderModel folder, FolderProductsViewModel products,
            FolderUsersViewModel users) : this(products)
        {
            CreationDate = folder.CreationDate.ToDefaultFormat();
            Author = folder.Author;
            Id = folder.Id;
            Name = folder.Name;
            OldName = folder.Name;
            Description = folder.Description;
            Color = folder.Color;

            Telegram = new(folder.Notifications.Telegram, Id);
            Products.InitFolderProducts(folder.Products);
            Cleanup = new FolderCleanupViewModel(folder);
            Alerts = new FolderAlertsViewModel(folder);
            Users = users;
        }


        internal List<ProductNodeViewModel> GetFolderProducts(TreeViewModel.TreeViewModel treeViewModel) =>
            Products?.GetProducts(treeViewModel) ?? new();

        internal FolderAdd ToFolderAdd(User author, TreeViewModel.TreeViewModel treeViewModel) =>
            new()
            {
                Name = Name,
                Color = Color,
                Description = Description,
                AuthorId = author.Id,
                Author = author.Name,
                Products = GetFolderProducts(treeViewModel).ToDictionary(f => f.Id),
            };

        internal FolderUpdate ToFolderUpdate(string initiator = null) =>
            new()
            {
                Id = Id,
                Color = Color,
                Name = IsNameChanged ? Name : null,
                Description = Description is null ? string.Empty : Description,
                Initiator = initiator ?? TreeValuesCache.System
            };
    }
}
