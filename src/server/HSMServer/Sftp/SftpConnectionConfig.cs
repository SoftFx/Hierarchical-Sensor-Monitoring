namespace HSMServer.Sftp
{
    public class SftpConnectionConfig
    {
        public bool IsEnabled { get; set; }

        public string Address { get; set; }

        public int? Port { get; set; }

        public string Username { get; set; }

        public string Password { get; set; }

        public string PrivateKey { get; set; }

        public string RootPath { get; set; }
    }
}
