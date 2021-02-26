using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using HSMServer.ClientUpdateService;
using HSMService;
using Microsoft.Extensions.Logging;

namespace HSMServer.Services
{
    public class AdminService : Admin.AdminBase
    {
        private readonly IUpdateService _updateService;
        private readonly ILogger<AdminService> _logger;
        private const int BLOCK_SIZE = 1048576;
        public AdminService(IUpdateService updateService, ILogger<AdminService> logger)
        {
            _updateService = updateService;
            _logger = logger;
            _logger.LogInformation("UpdateService initialized");
        }

        public override Task<UpdateInfoMessage> GetUpdateInfo(Empty request, ServerCallContext context)
        {
            return Task.FromResult(_updateService.GetUpdateInfo());
        }

        public override async Task GetUpdateStream(UpdateStreamRequestMessage request, IServerStreamWriter<UpdateStreamMessage> responseStream, ServerCallContext context)
        {
            byte[] bytes = _updateService.GetFileContents(request.FileName);
            int count = 0;
            int currentIndex = 0;
            int bytesLeft = bytes.Length;
            while (currentIndex < bytesLeft)
            {
                UpdateStreamMessage message = new UpdateStreamMessage();
                if (bytesLeft <= BLOCK_SIZE)
                {
                    message.BytesData = ByteString.CopyFrom(bytes);
                    message.BlockSize = bytesLeft;
                    message.BlockIndex = count;
                    currentIndex = bytesLeft;
                }
                else
                {
                    message.BytesData = ByteString.CopyFrom(bytes[currentIndex..(BLOCK_SIZE+currentIndex)]);
                    message.BlockIndex = count;
                    message.BlockSize = BLOCK_SIZE;
                    bytesLeft = bytesLeft - BLOCK_SIZE;
                    currentIndex = currentIndex + BLOCK_SIZE;
                }

                await responseStream.WriteAsync(message);

                ++count;
            }
        }
    }
}
