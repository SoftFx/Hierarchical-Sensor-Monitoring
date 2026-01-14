using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;
using HSMServer.Core.Model;
using MemoryPack;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PerformanceBenchmarks;

[ShortRunJob] // Для быстрого тестирования
[MemoryDiagnoser]
[KeepBenchmarkFiles] // Сохраняет временные файлы
public class SerializationSpeedBenchmarks
{
    private BooleanValue _testValue;
    private List<BooleanValue> _testList;
    private BooleanValueDto _memoryPackDto;
    private List<BooleanValueDto> _memoryPackDtoList;

    // Буферы для десериализации
    private byte[] _systemJsonBytes;
    private byte[] _messagePackBytes;
    private byte[] _memoryPackBytes;
    private byte[] _systemJsonListBytes;
    private byte[] _messagePackListBytes;
    private byte[] _memoryPackListBytes;

    [Params(1, 10, 100, 1000)]
    public int DataSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Создаем тестовые данные
        _testList = Enumerable.Range(0, DataSize).Select(i => new BooleanValue
        {
            Status = i % 3 == 0 ? SensorStatus.Ok : SensorStatus.Error,
            Value = i % 2 == 0,
            Comment = $"Sensor {i} reading with additional details for performance testing",
            Time = DateTime.UtcNow.AddHours(-i),
            ReceivingTime = DateTime.UtcNow,
            LastReceivingTime = i % 2 == 0 ? DateTime.UtcNow.AddMinutes(-i * 5) : null,
            AggregatedValuesCount = i + 1,
            IsTimeout = i % 5 == 0,
            EmaValue = i % 2 == 0 ? (double?)i * 0.1 : null
        }).ToList();

        _testValue = _testList[0];
        _memoryPackDto = BooleanValueDto.FromBooleanValue(_testValue);
        _memoryPackDtoList = _testList.Select(BooleanValueDto.FromBooleanValue).ToList();

        // Подготавливаем сериализованные данные для тестов десериализации
        PrepareSerializedData();
    }

    private void PrepareSerializedData()
    {
        // System.Text.Json
        _systemJsonBytes = JsonSerializer.SerializeToUtf8Bytes(_testValue);
        _systemJsonListBytes = JsonSerializer.SerializeToUtf8Bytes(_testList);

        // MessagePack
        var resolver = CompositeResolver.Create(
            new IMessagePackFormatter[] { new BooleanValueFormatter() },
            new[] { StandardResolver.Instance }
        );
        var options = MessagePackSerializerOptions.Standard.WithResolver(resolver);

        _messagePackBytes = MessagePackSerializer.Serialize(_testValue, options);
        _messagePackListBytes = MessagePackSerializer.Serialize(_testList, options);

        // MemoryPack
        _memoryPackBytes = MemoryPackSerializer.Serialize(_memoryPackDto);
        _memoryPackListBytes = MemoryPackSerializer.Serialize(_memoryPackDtoList);
    }

    // ========== СЕРИАЛИЗАЦИЯ ОДНОГО ОБЪЕКТА ==========

    [Benchmark(Description = "System.Text.Json Serialize", Baseline = true)]
    [BenchmarkCategory("Single", "Serialize")]
    public byte[] SystemTextJson_Serialize()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_testValue);
    }

    [Benchmark(Description = "MessagePack Serialize")]
    [BenchmarkCategory("Single", "Serialize")]
    public byte[] MessagePack_Serialize()
    {
        var resolver = CompositeResolver.Create(
            new IMessagePackFormatter[] { new BooleanValueFormatter() },
            new[] { StandardResolver.Instance }
        );
        var options = MessagePackSerializerOptions.Standard.WithResolver(resolver);
        return MessagePackSerializer.Serialize(_testValue, options);
    }

    [Benchmark(Description = "MemoryPack Serialize")]
    [BenchmarkCategory("Single", "Serialize")]
    public byte[] MemoryPack_Serialize()
    {
        return MemoryPackSerializer.Serialize(_memoryPackDto);
    }

    // ========== ДЕСЕРИАЛИЗАЦИЯ ОДНОГО ОБЪЕКТА ==========

    [Benchmark(Description = "System.Text.Json Deserialize")]
    [BenchmarkCategory("Single", "Deserialize")]
    public BooleanValue SystemTextJson_Deserialize()
    {
        return JsonSerializer.Deserialize<BooleanValue>(_systemJsonBytes);
    }

    [Benchmark(Description = "MessagePack Deserialize")]
    [BenchmarkCategory("Single", "Deserialize")]
    public BooleanValue MessagePack_Deserialize()
    {
        var resolver = CompositeResolver.Create(
            new IMessagePackFormatter[] { new BooleanValueFormatter() },
            new[] { StandardResolver.Instance }
        );
        var options = MessagePackSerializerOptions.Standard.WithResolver(resolver);
        return MessagePackSerializer.Deserialize<BooleanValue>(_messagePackBytes, options);
    }

    [Benchmark(Description = "MemoryPack Deserialize")]
    [BenchmarkCategory("Single", "Deserialize")]
    public BooleanValue MemoryPack_Deserialize()
    {
        var dto = MemoryPackSerializer.Deserialize<BooleanValueDto>(_memoryPackBytes);
        return dto.ToBooleanValue();
    }

    // ========== СЕРИАЛИЗАЦИЯ СПИСКА ==========

    [Benchmark(Description = "System.Text.Json Serialize List")]
    [BenchmarkCategory("List", "Serialize")]
    public byte[] SystemTextJson_Serialize_List()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_testList);
    }

    [Benchmark(Description = "MessagePack Serialize List")]
    [BenchmarkCategory("List", "Serialize")]
    public byte[] MessagePack_Serialize_List()
    {
        var resolver = CompositeResolver.Create(
            new IMessagePackFormatter[] { new BooleanValueFormatter() },
            new[] { StandardResolver.Instance }
        );
        var options = MessagePackSerializerOptions.Standard.WithResolver(resolver);
        return MessagePackSerializer.Serialize(_testList, options);
    }

    [Benchmark(Description = "MemoryPack Serialize List")]
    [BenchmarkCategory("List", "Serialize")]
    public byte[] MemoryPack_Serialize_List()
    {
        return MemoryPackSerializer.Serialize(_memoryPackDtoList);
    }

    // ========== ДЕСЕРИАЛИЗАЦИЯ СПИСКА ==========

    [Benchmark(Description = "System.Text.Json Deserialize List")]
    [BenchmarkCategory("List", "Deserialize")]
    public List<BooleanValue> SystemTextJson_Deserialize_List()
    {
        return JsonSerializer.Deserialize<List<BooleanValue>>(_systemJsonListBytes);
    }

    [Benchmark(Description = "MessagePack Deserialize List")]
    [BenchmarkCategory("List", "Deserialize")]
    public List<BooleanValue> MessagePack_Deserialize_List()
    {
        var resolver = CompositeResolver.Create(
            new IMessagePackFormatter[] { new BooleanValueFormatter() },
            new[] { StandardResolver.Instance }
        );
        var options = MessagePackSerializerOptions.Standard.WithResolver(resolver);
        return MessagePackSerializer.Deserialize<List<BooleanValue>>(_messagePackListBytes, options);
    }

    [Benchmark(Description = "MemoryPack Deserialize List")]
    [BenchmarkCategory("List", "Deserialize")]
    public List<BooleanValue> MemoryPack_Deserialize_List()
    {
        var dtos = MemoryPackSerializer.Deserialize<List<BooleanValueDto>>(_memoryPackListBytes);
        return dtos.Select(d => d.ToBooleanValue()).ToList();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Важно: используйте Console.Out напрямую
        var stdout = Console.Out;
        stdout.WriteLine($"System.Text.Json size {_systemJsonBytes.Length}");
        stdout.WriteLine($"System.Text.Json List size {_systemJsonListBytes.Length}");
        stdout.WriteLine($"MessagePack size {_messagePackBytes.Length}");
        stdout.WriteLine($"MessagePack List size {_messagePackListBytes.Length}");
        stdout.WriteLine($"MemoryPack size {_memoryPackBytes.Length}");
        stdout.WriteLine($"MemoryPack List size {_memoryPackListBytes.Length}");
    }

}

