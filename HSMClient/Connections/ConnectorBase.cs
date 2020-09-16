using System;
using System.Collections.Generic;
using System.Text;

namespace HSMClient.Connections
{
    public abstract class ConnectorBase
    {
        protected string _address;

        public ConnectorBase(string address)
        {
            _address = address;
        }

        public abstract object Get();
    }
}
