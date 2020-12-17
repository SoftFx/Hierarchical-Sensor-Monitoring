namespace HSMCommon.Certificates
{
    public class CertificateData
    {
        public CertificateData()
        {
            CountryName = string.Empty;
            StateOrProvinceName = string.Empty;
            LocalityName = string.Empty;
            OrganizationName = string.Empty;
            OrganizationUnitName = string.Empty;
            CommonName = string.Empty;
            EmailAddress = string.Empty;
        }
        public string CountryName { get; set; }
        public string StateOrProvinceName { get; set; }
        public string LocalityName { get; set; }
        public string OrganizationName { get; set; }
        public string OrganizationUnitName { get; set; }
        public string CommonName { get; set; }
        public string EmailAddress { get; set; }
    }
}
