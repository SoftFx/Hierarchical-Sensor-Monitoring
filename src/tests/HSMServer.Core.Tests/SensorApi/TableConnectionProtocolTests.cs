using HSMDatabase.AccessManager.TableBlocks;
using HSMServer.Core.TableSensors;
using HSMServer.SensorApi.Sessions;
using System;
using System.Buffers;
using System.Text;
using Xunit;

namespace HSMServer.Core.Tests.SensorApi
{
    // The frames of the table connection (#1845) as bytes, without a server: the ack writer and
    // the frame header reader. The strings are the contract written out.
    public class TableConnectionProtocolTests
    {
        [Fact]
        public void A_block_ack_lists_the_refused_tables_by_index_name_and_reason()
        {
            var output = new ArrayBufferWriter<byte>();

            SessionProtocol.WriteBlockAck(output, 17, "stored", 3, [new TableRefusalInfo(2, "quotes/X\"Y", TableRefusalReason.UnknownSensor)], refusedCount: 1);

            Assert.Equal("{\"ack\":17,\"status\":200,\"body\":{\"outcome\":\"stored\",\"tables\":3,\"refusedCount\":1,\"refused\":[{\"index\":2,\"table\":\"quotes/X\\u0022Y\",\"reason\":\"UnknownSensor\"}]}}",
                Encoding.UTF8.GetString(output.WrittenSpan));
        }

        // A duplicate whose first put's refusals cannot be told (today: its envelope cannot be read;
        // after #1850: merged and deleted) leaves the list out and says so.
        [Fact]
        public void A_duplicate_whose_refusals_are_not_known_leaves_the_list_out_and_says_so()
        {
            var output = new ArrayBufferWriter<byte>();

            SessionProtocol.WriteBlockAck(output, 18, "duplicate", 1000, refused: null, refusedCount: 0);

            Assert.Equal("{\"ack\":18,\"status\":200,\"body\":{\"outcome\":\"duplicate\",\"tables\":1000,\"refusedKnown\":false}}", Encoding.UTF8.GetString(output.WrittenSpan));

            var ack = TableAck.Parse(System.Text.Json.Nodes.JsonNode.Parse(output.WrittenSpan));

            Assert.Equal("duplicate", ack.Outcome);
            Assert.Null(ack.Refused);
        }

        [Fact]
        public void A_store_fault_ack_carries_the_time_to_wait()
        {
            var output = new ArrayBufferWriter<byte>();

            SessionProtocol.WriteAck(output, 21, 503, "the block store cannot take blocks now", 60);

            Assert.Equal("{\"ack\":21,\"status\":503,\"error\":\"the block store cannot take blocks now\",\"retryAfter\":60}", Encoding.UTF8.GetString(output.WrittenSpan));
        }

        [Fact]
        public void The_ordinary_hello_carries_the_session_id()
        {
            var output = new ArrayBufferWriter<byte>();
            var id = Guid.Parse("20e46170-0000-4000-8000-000000000001");

            SessionProtocol.WriteHello(output, 4194304, 30, id);

            Assert.Equal("{\"op\":\"hello\",\"body\":{\"maxFrameBytes\":4194304,\"heartbeatSeconds\":30,\"sessionId\":\"20e46170000040008000000000000001\"}}",
                Encoding.UTF8.GetString(output.WrittenSpan));
        }

        [Fact]
        public void A_frame_header_is_read_as_the_contract_lays_it_out()
        {
            var producer = Guid.NewGuid();
            var frame = TableConnectionTestClient.Frame(1, 0x0102030405060708, producer, long.MaxValue, [0x48, 0x54]);

            Assert.True(TableFrame.TryRead(frame, out var header, out var error), error);
            Assert.Equal(TableFrameKind.Block, header.Kind);
            Assert.Equal(0x0102030405060708, header.Id);
            Assert.Equal(producer, header.ProducerId);
            Assert.Equal(long.MaxValue, header.ProducerSequence);
            Assert.Equal(40, header.BlockOffset);

            // A later version may grow the header: the block starts where the header length says.
            frame = new byte[48];
            TableConnectionTestClient.Frame(2, 1, producer, 1, []).CopyTo(frame, 0);
            frame[4] = 48;

            Assert.True(TableFrame.TryRead(frame, out header, out error), error);
            Assert.Equal(48, header.BlockOffset);
        }
    }
}
