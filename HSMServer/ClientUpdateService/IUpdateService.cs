using HSMService;

namespace HSMServer.ClientUpdateService
{
    public interface IUpdateService
    {
        public UpdateInfoMessage GetUpdateInfo();
        public byte[] GetUpdateFile(int index);
        public byte[] GetFileContents(string fileName);
    }
}