﻿using System;
using System.Collections.Generic;
using HSMDatabase.Entity;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public interface IProductEntity
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public DateTime DateAdded { get; set; }
        public List<ExtraKeyEntity> ExtraKeys { get; set; }
    }
}
