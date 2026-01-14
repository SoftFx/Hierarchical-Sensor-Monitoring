using HSMServer.Core.Model;
using MessagePack;
using MessagePack.Formatters;

namespace PerformanceBenchmarks;

public class BooleanValueFormatter : IMessagePackFormatter<BooleanValue>
{
    public void Serialize(ref MessagePackWriter writer, BooleanValue value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(9);
        writer.Write(value.ReceivingTime);
        writer.Write((byte)value.Status);
        writer.Write(value.Comment ?? string.Empty);
        writer.Write(value.Time);
        writer.Write(value.IsTimeout);

        if (value.LastReceivingTime.HasValue)
        {
            writer.Write(value.LastReceivingTime.Value);
        }
        else
        {
            writer.WriteNil();
        }

        writer.Write(value.AggregatedValuesCount);

        if (value.EmaValue.HasValue)
        {
            writer.Write(value.EmaValue.Value);
        }
        else
        {
            writer.WriteNil();
        }

        writer.Write(value.Value);
    }

    public BooleanValue Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        reader.ReadArrayHeader(); // Пропускаем header

        return new BooleanValue
        {
            ReceivingTime = reader.ReadDateTime(),
            Status = (SensorStatus)reader.ReadByte(),
            Comment = reader.ReadString(),
            Time = reader.ReadDateTime(),
            IsTimeout = reader.ReadBoolean(),
            LastReceivingTime = reader.TryReadNil() ? null : reader.ReadDateTime(),
            AggregatedValuesCount = reader.ReadInt64(),
            EmaValue = reader.TryReadNil() ? null : reader.ReadDouble(),
            Value = reader.ReadBoolean()
        };
    }
}
