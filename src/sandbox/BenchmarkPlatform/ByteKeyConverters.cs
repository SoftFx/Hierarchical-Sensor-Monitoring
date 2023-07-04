using System;
using System.Collections.Generic;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace TestLevelDB;

[MemoryDiagnoser]
public class ByteKeyConverters
{
    private List<Guid> _guids;

    private List<byte[]> _results;


    [Params(100, 1000, 10000, 100000)] 
    public int N { get; set; }


    [GlobalSetup]
    public void GenerateGuids()
    {
        _guids = new List<Guid>(N);
        
        for (long i = 0; i < N; i++)
            _guids.Add(Guid.NewGuid());
    }

    [IterationSetup]
    public void ResultInit() => _results = new (N);



    [Benchmark]
    public List<byte[]> SpanConverter()
    {
        Span<byte> result = stackalloc byte[16 + sizeof(long)];

        for (long i = 0; i < N; i++)
        {
            _guids[(int)i].TryWriteBytes(result);
            BitConverter.TryWriteBytes(result[16..], i);

            _results.Add(result.ToArray());
        }

        return _results;
    }

    [Benchmark]
    public List<byte[]> BlockCopyConverter()
    {
        for (long i = 0; i < N; i++)
        {
            var guidBytes = _guids[(int)i].ToByteArray();
            var timeBytes = BitConverter.GetBytes(i);
            var result = new byte[guidBytes.Length + timeBytes.Length];

            Buffer.BlockCopy(guidBytes, 0, result, 0, guidBytes.Length);
            Buffer.BlockCopy(timeBytes, 0, result, guidBytes.Length, timeBytes.Length);

            _results.Add(result);
        }

        return _results;
    }

    [Benchmark]
    public List<byte[]> StringConverter()
    {
        for (long i = 0; i < N; i++)
        {
            var guid = _guids[(int)i];
            _results.Add(Encoding.UTF8.GetBytes($"{guid}_{i:D19}"));
        }

        return _results;
    }
}