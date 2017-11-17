﻿using System.Collections.Generic;

namespace CloneDeploy_Common.DbUpgrades
{
    public class VersionMapping
    {
        private readonly Dictionary<int, int> _mapping;

        public VersionMapping()
        {
            _mapping  = new Dictionary<int,int>();
            _mapping.Add(130, 1300);
            _mapping.Add(131, 1301);
            _mapping.Add(132, 1302);
        }

        public Dictionary<int, int> Get()
        {
            return _mapping;  
        }
    }
}
