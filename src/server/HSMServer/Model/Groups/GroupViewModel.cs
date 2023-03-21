using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace HSMServer.Model.Groups
{
    public class GroupViewModel
    {
        public List<SelectListItem> AllProducts { get; init; }

        public string CreationDate { get; init; }

        public string Author { get; init; }


        public Guid? Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public Color Color { get; set; }

        public List<string> Products { get; set; } = new();


        public GroupViewModel() { }

        internal GroupViewModel(GroupModel group)
        {
            CreationDate = group.CreationDate.ToDefaultFormat();
            Id = group.Id;
            Name = group.Name;
            Description = group.Description;
            Color = group.Color;

        }
    }
}
