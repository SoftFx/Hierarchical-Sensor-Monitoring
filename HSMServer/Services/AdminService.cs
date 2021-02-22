using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AdminService;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using HSMServer.ClientUpdateService;
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
            byte[] bytes = _updateService.GetUpdateFile(request.FileIndex);
            int count = 0;
            int currentIndex = 0;
            while (!context.CancellationToken.IsCancellationRequested || currentIndex <= bytes.Length)
            {
                UpdateStreamMessage message = new UpdateStreamMessage();
                message.BlockSize = BLOCK_SIZE;
                message.BlockIndex = count;
                message.BytesData = ByteString.CopyFrom(bytes.Skip(count * BLOCK_SIZE).Take(BLOCK_SIZE).ToArray());

                await responseStream.WriteAsync(message);

                currentIndex = currentIndex + BLOCK_SIZE;
                ++count;
            }
        }
    }
}
