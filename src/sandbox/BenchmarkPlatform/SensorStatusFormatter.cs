using HSMServer.Core.Model;
using MessagePack;
using MessagePack.Formatters;

namespace PerformanceBenchmarks;

public class SensorStatusFormatter : IMessagePackFormatter<SensorStatus>
{
    public void Serialize(ref MessagePackWriter writer, SensorStatus value, MessagePackSerializerOptions options)
    {
        writer.Write((byte)value);
    }

    public SensorStatus Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        return (SensorStatus)reader.ReadByte();
    }
}
