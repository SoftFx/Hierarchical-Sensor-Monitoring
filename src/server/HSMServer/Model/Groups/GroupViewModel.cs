using System;
using System.Drawing;

namespace HSMServer.Model.Groups
{
    public class GroupViewModel
    {
        public string Author { get; init; }

        public string CreationDate { get; init; }


        public Guid? Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public Color Color { get; set; }


        public GroupViewModel() { }
    }
}
