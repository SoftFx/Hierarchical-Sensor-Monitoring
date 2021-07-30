using System;
using System.Collections.Generic;

namespace HSMDatabase.Entity
{
    public class ProductEntity
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public DateTime DateAdded { get; set; }
        public List<ExtraKeyEntity> ExtraKeys { get; set; }
    }
}