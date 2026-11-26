using System;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Helpers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // #1883: the file actions of the web UI never give a large value out as a file: its content is in the large file
    // store, and its download is step 3's (#1885). The sensor is resolved through the user's roles first, as for every
    // other value (WebAccessFixture, HomeControllerAccessTests).
    public class HomeControllerLargeFileTests
    {
        [Fact]
        public async Task The_file_and_its_stream_answer_404_for_a_large_last_value_and_the_info_describes_it()
        {
            var tree = new WebAccessFixture();
            var value = new LargeFileValue
            {
                Time = DateTime.UtcNow,
                Value = [],
                Name = "app.dmp",
                Extension = "dmp",
                OriginalSize = 8L << 30,
                FileId = Guid.NewGuid(),
                Hash = new byte[32],
            };

            Assert.True(tree.Report.TryAddValue(value));

            var controller = tree.Home(tree.Admin);
            var id = SensorPathHelper.EncodeGuid(tree.Report.Id);

            Assert.IsType<NotFoundResult>(await controller.GetFile(id));
            Assert.IsType<NotFoundResult>(await controller.GetFileStream(id));

            var info = Assert.IsType<LargeFileValue>(controller.GetFileInfo(id).Value);
            Assert.Equal(value.FileId, info.FileId);
            Assert.Empty(info.Value);
        }
    }
}
