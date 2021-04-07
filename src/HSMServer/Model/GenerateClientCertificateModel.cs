using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Model
{
    public class GenerateClientCertificateModel
    {
        public string CountryName { get; set; }
        public string StateOrProvinceName { get; set; }
        public string LocalityName { get; set; }
        public string OrganizationName { get; set; }
        public string OrganizationUnitName { get; set; }
        public string CommonName { get; set; }
        public string EmailAddress { get; set; }
    }
}
